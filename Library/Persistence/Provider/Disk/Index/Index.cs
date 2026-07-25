using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using HackyMessage.Extension;
using HackyMessage.Serialization;
using Microsoft.VisualStudio.Threading;
using Serilog;

namespace HackyMessage.Persistence.Provider.Disk.Index;

public sealed class Index : IIndexProvider
{
    private readonly ILogger Logger = Log.Logger.ForFriendlyContext<Index>();
    
    private readonly ConcurrentDictionary<short, long> _values = new();
    private readonly AsyncReaderWriterLock _storeLock = new();
    private int _isSnapshotting = 0;
    
    private readonly byte[] _magicHeader = [0xD0, 0x0D, 0xFE, 0xED];
    private readonly string _filePath;
    private readonly int _maxSize;

    private FileStream _writer = null!;
    private FileStream _reader = null!;

    public FileStream Writer => _writer;
    public FileStream Reader => _reader;

    public Index(string filePath, int maxSize = 4096)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _maxSize = maxSize;
        OpenStreams();
    }

    private void OpenStreams()
    {
        _reader = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Write,
            bufferSize: 4096, FileOptions.Asynchronous);

        _writer = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
            bufferSize: 0, FileOptions.WriteThrough | FileOptions.Asynchronous);

        _writer.Seek(0, SeekOrigin.End);
    }

    public async Task AdvanceAsync(short key, long value, CancellationToken ct = default)
    {
        var snapshotNeeded = false;
        await using (await _storeLock.WriteLockAsync(ct))
        {
            _values.TryGetValue(key, out var oldValue);
            if (oldValue >= value) return;
                
            await WriteEntryAsync(_writer, key, value, ct);
            _values.AddOrUpdate(key, (_) => value, (_, _) => value);
            
            snapshotNeeded = _writer.Position >= _maxSize;
        }
        
        if (snapshotNeeded && Interlocked.CompareExchange(ref _isSnapshotting, 1, 0) == 0)
        {
            await SafeSnapshotAsync(CancellationToken.None);
        }
    }

    public async Task<long> GetOrDefaultAsync(short key, long defaultValue, CancellationToken ct)
    {
        await using var lockHandle = await _storeLock.ReadLockAsync(ct);
        return _values.GetValueOrDefault(key, defaultValue);
    }

    public async Task ReplayAsync(CancellationToken ct)
    {
        await using var lockHandle = await _storeLock.WriteLockAsync(ct);
        
        _reader.Position = 0;
        while (true)
        {
            var readLimit = _writer.Position;
            if (!await StreamSynchronizer.SyncToNextHeaderAsync(_reader, _magicHeader, readLimit, ct))
            {
                return; // EOF reached
            }
            
            var frameStart = _reader.Position;
            try
            {
                var (key, value) = await ReadEntryAsync(ct);
                _values.AddOrUpdate(key, (_) => value, (_, _) => value);
            }
            catch (InvalidDataException)
            {
                // Skip one byte and try to find the next magic header
                _reader.Position = frameStart + 1;
            }
            catch (EndOfStreamException)
            {
                _reader.Position = frameStart;
                return;
            }
        }
    }

    private async Task SafeSnapshotAsync(CancellationToken ct)
    {
        try
        {
            await using var lockHandle = await _storeLock.WriteLockAsync(ct);
            
            var tempFilePath = _filePath + ".tmp";
        
            // 1. Write the new snapshot to a temporary file
            await using (var tempStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 
                             bufferSize: 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                foreach (var kvp in _values)
                    await WriteEntryAsync(tempStream, kvp.Key, kvp.Value, ct);
                
                await tempStream.FlushAsync(ct);
            }

            // 2. Dispose current streams to release file locks
            await _writer.DisposeAsync();
            await _reader.DisposeAsync();

            // 3. Atomically replace the old index file with the new snapshot
            File.Move(tempFilePath, _filePath, overwrite: true);

            // 4. Reopen streams
            OpenStreams();
            _reader.Position = 0;
        }
        finally
        {
            Volatile.Write(ref _isSnapshotting, 0);
        }
    }

    private async Task<(short key, long value)> ReadEntryAsync(CancellationToken ct)
    {
        // ... (unchanged method contents, uses _reader)
        var poolBuffer = ArrayPool<byte>.Shared.Rent(14);
        var memoryBuffer = poolBuffer.AsMemory().Slice(0, 14);
        try
        {
            await _reader.ReadExactlyAsync(memoryBuffer, ct);
            var key = BinaryPrimitives.ReadInt16BigEndian(memoryBuffer.Span.Slice(0, 2));
            var value = BinaryPrimitives.ReadInt64BigEndian(memoryBuffer.Span.Slice(2, 8));
            var crc = BinaryPrimitives.ReadUInt32BigEndian(memoryBuffer.Span.Slice(10, 4));
            
            if (!Crc32ChecksumUtility.Verify(memoryBuffer.Span.Slice(0, 10), crc))
            {
                Logger.Error("Entry failed checksum verification");
                throw new InvalidDataException("Invalid CRC checksum in index.");
            }

            return (key, value);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
        }
    }

    // Refactored to accept a target Stream so it can write to both _writer and temp streams
    private async Task WriteEntryAsync(Stream stream, short key, long value, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(18);
        try
        {
            var span = buffer.AsSpan().Slice(0, 18);

            _magicHeader.CopyTo(span.Slice(0, 4));
            BinaryPrimitives.WriteInt16BigEndian(span.Slice(4, 2), key);
            BinaryPrimitives.WriteInt64BigEndian(span.Slice(6, 8), value);

            var checksum = Crc32ChecksumUtility.Compute(span.Slice(4, 10));
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(14, 4), checksum);

            await stream.WriteAsync(buffer.AsMemory().Slice(0, 18), ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        _storeLock.Dispose();
        _writer?.Dispose();
        _reader?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _storeLock.Dispose();
        if (_writer != null) await _writer.DisposeAsync();
        if (_reader != null) await _reader.DisposeAsync();
    }
}