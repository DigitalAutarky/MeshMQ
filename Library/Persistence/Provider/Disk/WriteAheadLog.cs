using DotNext.Threading;
using HackyMessage.Extension;
using HackyMessage.Persistence.Provider.Disk.Index;
using HackyMessage.Pooled;
using HackyMessage.Pooled.Pool;
using HackyMessage.Serialization.Serializers;
using Serilog;
using AsyncAutoResetEvent = DotNext.Threading.AsyncAutoResetEvent;
using Timeout = System.Threading.Timeout;

namespace HackyMessage.Persistence.Provider.Disk;

public sealed class WriteAheadLog<T>
(IoContext ioContext, Action onFullyConsumed) : IPersistenceProvider<T>
{
    private readonly ILogger _logger = Log.Logger.ForFriendlyContext<WriteAheadLog<T>>();
    
    private readonly CancellationTokenSourcePool _ctsPool = new(1);
    private readonly ArrayBufferWriterPool<byte> _writeBufferPool = new(1, 256, true);
    private readonly ArrayBufferWriterPool<byte> _readBufferPool = new(1, 256, false);
    private readonly DisposableArrayPool<bool> _disposableBoolArrayPool = new();
    
    private readonly BufferTransactionPool<byte> _writeBufferTransactionPool = new(1, true);
    private readonly BufferTransactionPool<byte> _readBufferTransactionPool = new(1, false);
    private readonly StreamTransactionPool _streamTransactionPool = new(1);
    
    private readonly WriteAheadLogFrameSerializer<T> _writeAheadLogFrameSerializer = new ();
    private readonly AsyncAutoResetEvent _uncommittedDataSignal = new(false);
    
    private readonly AsyncManualResetEvent _writerGate = new(initialState: true);
    private readonly long _highWatermark = ioContext.highWatermark;
    private readonly long _lowWatermark = ioContext.lowWatermark;
    private long _writersAwaitingCapacity = 0L;
    
    private long _currentWritePosition = ioContext.LogWriter.Position;
    private long _currentReadPosition = ioContext.LogReader.Position;
    private long _currentExecutionPosition = ioContext.LogReader.Position;
    
    private bool IsFull => Interlocked.Read(ref _currentWritePosition) >= ioContext.MaxSize;
    private bool IsCaughtUp => Interlocked.Read(ref _currentReadPosition) == Interlocked.Read(ref _currentWritePosition);
    private bool IsFullyProcessed => IsFull && IsCaughtUp;
    
    bool IPersistenceProvider<T>.IsFull() => IsFull;
    bool IPersistenceProvider<T>.IsFullyProcessed() => IsFullyProcessed;
    long IPersistenceProvider<T>.WritersAwaitingCapacity() => Interlocked.Read(ref _writersAwaitingCapacity);

    //TODO: switch to channel once caught up with on disk items re-feeded during startup
    public async Task<int> EnqueueAsync(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, bool isBlocking,  CancellationToken ct = default)
    {
        // Phase 0: Apply Backpressure if necessary
        // In non-blocking mode we bail out immediately if backpressure is turned on
        try
        {
            if (!await ApplyBackPressure(isBlocking, ct))
            {
                FailEntireBatch(items, CachedPersistenceResult.RetryLater);
                return 0;
            }
        }
        catch (OperationCanceledException)
        {
            FailEntireBatch(items, CachedPersistenceResult.Cancelled);
            return 0;
        }

        // Phase 1: Serialize and track which items succeeded serialization
        await using var bufferLease = _writeBufferPool.Rent();
        await using var bufferTransaction = _writeBufferTransactionPool.Rent(bufferLease.Buffer);
        await using var successfullySerializedLease = _disposableBoolArrayPool.Rent(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            try
            {
                _writeAheadLogFrameSerializer.SerializeSync(bufferTransaction.Buffer, items.Span[i].Item!, ioContext.MaxSize);
                bufferTransaction.Buffer.Commit();
                successfullySerializedLease.Array.Span[i] = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to serialize message of type {Type}", typeof(T));
                bufferTransaction.Buffer.Rollback();
                items.Span[i].CompletionSource?.TrySetResult(CachedPersistenceResult.SerializationFailure);
                successfullySerializedLease.Array.Span[i] = false;
            }
        }

        // Phase 2: Atomic Write
        var bytesWritten = 0;
        try
        {
            using var writeLock = await ioContext.WriteLock.AcquireAsync(Timeout.InfiniteTimeSpan, ct);

            if (IsFull)
            {
                var result = CachedPersistenceResult.PersistenceCapacityReached;
                FailRemainingItems(items, successfullySerializedLease.Array, result);
                return bytesWritten;
            }

            await using var transactionLease = _streamTransactionPool.Rent(ioContext.LogWriter);
            await transactionLease.Stream.WriteAsync(bufferLease.Buffer.WrittenMemory, CancellationToken.None);
            await transactionLease.Stream.FlushAsync(CancellationToken.None);

            // Success: Resolve ONLY the ones that made it through Phase 1
            transactionLease.Stream.Commit();
            bytesWritten = bufferLease.Buffer.WrittenMemory.Length;
            
            await ioContext.Index.AdvanceAsync(IndexKey.WritePosition, ioContext.LogWriter.Position, CancellationToken.None);
            Interlocked.Exchange(ref _currentWritePosition, ioContext.LogWriter.Position);

            BlockWriterIfAboveHighWatermark();
            ResolveSuccessfulItems(items, successfullySerializedLease.Array);
            _uncommittedDataSignal.Set();
        }
        catch (OperationCanceledException)
        {
            // Capture graceful cancellation during shutdown/disposal
            var result = CachedPersistenceResult.Cancelled;
            FailRemainingItems(items, successfullySerializedLease.Array, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "File write failed. Rolling back stream.");
            var result = CachedPersistenceResult.PersistenceFailure;
            FailRemainingItems(items, successfullySerializedLease.Array, result);
        }

        return bytesWritten;
    }

    public async Task<(int count, long readPosition)> DequeueAsync(T[] buffer, TimeSpan timeout, CancellationToken ct = default)
    {
        var count = 0;
        var readPosition = -1L;
        var deadline = CalculateDeadline(timeout);
        while (count < buffer.Length && !ct.IsCancellationRequested)
        {
            var itemOrNone = await TryGetNextItemAsync(deadline);
            if (itemOrNone.TryGetValue(out Item<T> value))
            {
                buffer[count++] = value.item;
                readPosition = value.readPosition;
                continue; 
            }

            if (IsFullyProcessed)
                break;
            
            try 
            {
                var remainingTime = GetRemainingTime(deadline);
                var hasMore = await _uncommittedDataSignal.WaitAsync(remainingTime, ct);
                if (!hasMore)
                    break;
            }
            catch (OperationCanceledException) { break; }
        }

        return (count, readPosition);
    }
    
    private async Task<ItemOrNone<T>> TryGetNextItemAsync(DateTime deadline)
    {
        // 0. Acquire Lock Within Remaining Time Or Return
        var remainingTime = GetRemainingTime(deadline);
        using var readLock = await ioContext.ReadLock.AcquireAsync(remainingTime);
        if (readLock.IsEmpty) return new None(); //not acquired in time
        
        // 1. Abort if there is nothing to consume so we dont throw an
        // unnecessary EndOfFileException. This check must happen inside the lock
        if (IsCaughtUp) return new None(); //nothing to read
        
        // 2. Setup Resources
        await using var bufferWriterLease = _readBufferPool.Rent();
        await using var bufferTransactionLease = _readBufferTransactionPool.Rent(bufferWriterLease.Buffer);
        await using var streamTransactionLease = _streamTransactionPool.Rent(ioContext.LogReader);
        
        // 3. Volatile Domain Boundary: Isolate exception handling strictly to I/O and parsing
        try
        {
            var readLimit = Interlocked.Read(ref _currentWritePosition);
            var item = await _writeAheadLogFrameSerializer.DeserializeAsync(
                stream: streamTransactionLease.Stream,
                buffer: bufferTransactionLease.Buffer,
                maxLength: ioContext.MaxSize,
                readLimit: readLimit);

            // Commit the operations after a verified successful read
            bufferTransactionLease.Buffer.Commit();
            streamTransactionLease.Stream.Commit();
            Interlocked.Exchange(ref _currentReadPosition, ioContext.LogReader.Position);
            return new Item<T>(item, ioContext.LogReader.Position);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Encountered exception while trying to read next item of type {Type}", typeof(T));
            return new None();
        }
    }
    
    public async ValueTask ConfirmProcessed(long readPosition, CancellationToken ct = default)
    {
        _logger.Debug("Commiting processed offset to index ({Processed})", readPosition);
        await ioContext.Index.AdvanceAsync(IndexKey.ReadPosition, readPosition, ct);
    }
    
    public ValueTask NotifyExecutionComplete(long readPosition, CancellationToken ct = default)
    {
        Interlocked.Exchange(ref _currentExecutionPosition, readPosition);
        UnblockWriterIfBelowLowWatermark();
        return ValueTask.CompletedTask;
    }

    private void FailEntireBatch(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, PersistenceResult result)
    {
        for (var i = 0; i < items.Length; i++)
        {
            items.Span[i].CompletionSource!.TrySetResult(result);
        }
    }
    
    private static void ResolveSuccessfulItems(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, Memory<bool> serializedFlags)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (serializedFlags.Span[i])
                items.Span[i].CompletionSource!.TrySetResult(CachedPersistenceResult.Success);
        }
    }

    private static void FailRemainingItems(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, Memory<bool> serializedFlags, PersistenceResult result)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (serializedFlags.Span[i])
                items.Span[i].CompletionSource!.TrySetResult(result);
        }
    }
    
    private void BlockWriterIfAboveHighWatermark()
    {
        var currentWritePosition = Interlocked.Read(ref _currentWritePosition);
        var currentExecutionPosition = Interlocked.Read(ref _currentExecutionPosition);
        if (currentWritePosition - currentExecutionPosition >= _highWatermark)
        {
            _writerGate.Reset();
        }
    }

    private void UnblockWriterIfBelowLowWatermark()
    {
        var currentWritePosition = Interlocked.Read(ref _currentWritePosition);
        var currentExecutionPosition = Interlocked.Read(ref _currentExecutionPosition);
        if (currentWritePosition - currentExecutionPosition <= _lowWatermark)
        {
            _writerGate.Set();
        }
    }
    
    private async ValueTask<bool> ApplyBackPressure(bool isBlocking, CancellationToken ct = default)
    {
        if (isBlocking)
            return await _writerGate.WaitAsync(Timeout.InfiniteTimeSpan, ct);
        
        ct.ThrowIfCancellationRequested();
        return _writerGate.IsSet;
    }

    private DateTime CalculateDeadline(TimeSpan timeout)
    {
        return timeout == Timeout.InfiniteTimeSpan 
            ? DateTime.MaxValue 
            : DateTime.UtcNow.Add(timeout);
    }

    private static TimeSpan GetRemainingTime(DateTime deadline)
    {
        if (deadline == DateTime.MaxValue) return Timeout.InfiniteTimeSpan;
        var remaining = deadline.Subtract(DateTime.UtcNow);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Dispose()
    {
        // 1. Capture the state BEFORE disposing the streams
        var isFullyProcessedAtDisposal = false;
        try { isFullyProcessedAtDisposal = IsFullyProcessed; } catch { /* Ignore if already disposed */ }

        // 2. Dispose resources
        ioContext.Dispose();
        
        _writeBufferPool.Dispose();
        _readBufferPool.Dispose();
        _ctsPool.Dispose();
        
        _writeBufferTransactionPool.Dispose();
        _streamTransactionPool.Dispose();
        
        _writeAheadLogFrameSerializer.Dispose();
        _uncommittedDataSignal.Dispose();
        _writerGate.Dispose();

        // 3. Trigger callback based on captured state
        if (isFullyProcessedAtDisposal)
        {
            onFullyConsumed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Capture the state BEFORE disposing the streams
        var isFullyProcessedAtDisposal = false;
        try { isFullyProcessedAtDisposal = IsFullyProcessed; } catch { /* Ignore if already disposed */ }

        // 2. Dispose resources
        await ioContext.DisposeAsync();
        
        _writeBufferPool.Dispose();
        _readBufferPool.Dispose();
        _ctsPool.Dispose();
        
        _writeBufferTransactionPool.Dispose();
        _streamTransactionPool.Dispose();
        
        _writeAheadLogFrameSerializer.Dispose();
        await _uncommittedDataSignal.DisposeAsync();
        _writerGate.Dispose();

        // 3. Trigger callback based on captured state
        if (isFullyProcessedAtDisposal)
        {
            onFullyConsumed?.Invoke();
        }
    }
}