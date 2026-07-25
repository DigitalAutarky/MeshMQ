using System.Threading.Channels;
using HackyMessage.Common;
using HackyMessage.Core.Queue.DeadLetterQueue;
using HackyMessage.Core.Strategy;
using HackyMessage.Core.Strategy.Execution;
using HackyMessage.Extension;
using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Disk;
using HackyMessage.Pooled;
using HackyMessage.Pooled.Pool;
using Serilog;

namespace HackyMessage.Core.Partition;

public sealed class Partition<T>: IAsyncDisposable
{
    private readonly ILogger Logger = Log.Logger.ForFriendlyContext<Partition<T>>();
    
    private readonly CancellationTokenSource _cts;
    private readonly ValueTaskCompletionSourcePool<PersistenceResult> _csPool;
    private readonly WorkItemPool<Envelope<T>, PersistenceResult> _itemPool;

    private readonly IPersistenceProvider<Envelope<T>> _wal;
    private readonly Channel<WorkItem<Envelope<T>, PersistenceResult>> _blockingBuffer;
    private readonly Channel<WorkItem<Envelope<T>, PersistenceResult>> _nonBlockingBuffer;

    private readonly IDeadLetterHandler<T> _deadLetterHandler;
    
    private readonly List<Task> _backgroundTasks = [];
    
    public int InMemoryBufferCount => 
        (_blockingBuffer?.Reader.Count ?? 0) + (_nonBlockingBuffer?.Reader.Count ?? 0);

    
    public Partition(IPersistenceProvider<Envelope<T>> wal, IConsumer<T> consumer, IDeadLetterHandler<T> deadLetterHandler)
    {
        _cts = new CancellationTokenSource();
        _csPool = new ValueTaskCompletionSourcePool<PersistenceResult>(4 * consumer.PartitionWritePolicy.BufferSize);
        _itemPool = new WorkItemPool<Envelope<T>, PersistenceResult>(4 * consumer.PartitionWritePolicy.BufferSize);

        _wal = wal;
        _deadLetterHandler = deadLetterHandler;
        
        _blockingBuffer = consumer.PartitionWritePolicy.Create<WorkItem<Envelope<T>, PersistenceResult>>
            (false, true).Channel;
        
        _nonBlockingBuffer = consumer.PartitionWritePolicy.Create<WorkItem<Envelope<T>, PersistenceResult>>
            (false, true).Channel;

        //setup background tasks to feed items to the wal in a blocking manner
        //TODO: implement batcher without cancellationToken
        _backgroundTasks.Add(ChannelBatcher.ProcessInBatchesAsync(
            EnqueueBatchBlockingAsync,
            _blockingBuffer.Reader,
            consumer.PartitionWritePolicy.MaxDelayInterval,
            consumer.PartitionWritePolicy.BufferSize,
            _cts.Token));
        
        //setup background tasks to feed items to the wal in a non-blocking manner
        _backgroundTasks.Add(ChannelBatcher.ProcessInBatchesAsync(
            EnqueueBatchNonBlockingAsync,
            _nonBlockingBuffer.Reader,
            consumer.PartitionWritePolicy.MaxDelayInterval,
            consumer.PartitionWritePolicy.BufferSize,
            _cts.Token));
        
        //setup background task to consume and process items from the wal
        _backgroundTasks.Add(ProcessItemsAsync(
            strategy: consumer.ExecutionPolicy.Create(),
            registerFailure: OnFailedProcessing,
            consumer: consumer,
            ct: _cts.Token,
            persistence: _wal));
    }
    
    private async Task EnqueueBatchBlockingAsync(ReadOnlyMemory<WorkItem<Envelope<T>, PersistenceResult>> items)
        => await _wal.EnqueueAsync(items, isBlocking: true, ct: _cts.Token);

    private async Task EnqueueBatchNonBlockingAsync(ReadOnlyMemory<WorkItem<Envelope<T>, PersistenceResult>> items)
        => await _wal.EnqueueAsync(items, isBlocking: false, ct: _cts.Token);
    
    private async Task<bool> OnFailedProcessing(Envelope<T> item, Exception error)
        => await _deadLetterHandler.TryAddAsync(item, error);
    
    public async ValueTask<AcceptResult> AcceptAsync(Envelope<T> item)
    {
        if (_cts.Token.IsCancellationRequested)
            return CachedAcceptResult.Unavailable;

        await using var csLease = _csPool.Rent();
        await using var itemLease = _itemPool.Rent(item, csLease.Source);

        try
        {
            await _blockingBuffer.Writer.WriteAsync(itemLease.WorkItem, _cts.Token);
            var result = await csLease.Source.ValueTask;
            return ToResult(result);
        }
        catch (OperationCanceledException)
        {
            return CachedAcceptResult.Cancelled;
        }
    }
    
    public async ValueTask<AcceptResult> TryAcceptAsync(Envelope<T> item)
    {
        if (_cts.Token.IsCancellationRequested)
            return CachedAcceptResult.Unavailable;

        await using var csLease = _csPool.Rent();
        await using var itemLease = _itemPool.Rent(item, csLease.Source);

        var success = _nonBlockingBuffer.Writer.TryWrite(itemLease.WorkItem);
        if (!success) return CachedAcceptResult.RetryLater;

        var result = await csLease.Source.ValueTask;
        return ToResult(result);
    }

    // Todo: test confirmation modes
    private static async Task ProcessItemsAsync(
        IPersistenceProvider<Envelope<T>> persistence,
        IConsumer<T> consumer,
        IExecutionStrategy<T,Envelope<T>> strategy,
        Func<Envelope<T>, Exception, Task<bool>> registerFailure,
        CancellationToken ct)
    {
        var buffer = new Envelope<T>[strategy.BatchSize];
        var timeout = strategy.MaxDelayInterval;
        var mode = strategy.ExecutionMode;
        while (!ct.IsCancellationRequested)
        {
            var (count, readPosition) = await persistence.DequeueAsync(buffer, timeout, ct);
            if (count <= 0) continue;

            if (mode == ExecutionMode.AtMostOnce)
                await persistence.ConfirmProcessed(readPosition, CancellationToken.None);
            
            try
            {
                await strategy.ExecuteAsync(
                    messages: buffer.AsMemory(0, count),
                    registerFailure: registerFailure,
                    consumer: consumer);
            }
            finally
            {
                await persistence.NotifyExecutionComplete(readPosition, CancellationToken.None);
            }
            
            if (mode == ExecutionMode.AtLeastOnce)
                await persistence.ConfirmProcessed(readPosition, CancellationToken.None);
        }
    }

    // Todo test all mappings for both accept methods
    private static AcceptResult ToResult(PersistenceResult result)
    {
        return result switch
        {
            Persistence.Provider.Disk.Success _ => CachedAcceptResult.Success,
            RetryLater _ => CachedAcceptResult.RetryLater,
            SerializationFailure _ => CachedAcceptResult.Failed,
            PersistenceCapacityReached _ => CachedAcceptResult.Failed,
            PersistenceFailure _ => CachedAcceptResult.Failed,
            Persistence.Provider.Disk.Cancelled _ => CachedAcceptResult.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop accepting new messages and Signal background tasks to wrap up
        await _cts.CancelAsync();
        
        // 2. Stop the channels (not really needed but..)
        _blockingBuffer.Writer.Complete();
        _nonBlockingBuffer.Writer.Complete();

        // 3. Wait for the ChannelBatchers to drain and the Consumer to finish its current loop
        await WaitForBackgroundTasks(_backgroundTasks); 

        // 4. Safely dispose infrastructure now that all writing/reading has stopped
        await _wal.DisposeAsync();
        await CastAndDispose(_csPool);
        await CastAndDispose(_itemPool);
        await CastAndDispose(_cts);
        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
                await resourceAsyncDisposable.DisposeAsync();
            else
                resource.Dispose();
        }

        async ValueTask WaitForBackgroundTasks(List<Task> tasks)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) {} // Expected because ChannelBatcher throws ct.ThrowIfCancellationRequested() at the end
            catch (Exception e)
            {
                Logger.Error(e, "Failure during shutdown of partition background workers");
                throw;
            }
        }
    }
}