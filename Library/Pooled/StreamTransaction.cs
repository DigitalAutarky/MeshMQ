using HackyMessage.Extension;
using HackyMessage.Serialization;

namespace HackyMessage.Pooled;

public sealed class StreamTransaction
{
    private Stream _innerStream = null;
    private long _committedPosition = -1;
    private long _committedLength = -1;

    public void Activate(Stream stream)
    {
        _innerStream = stream ?? throw new ArgumentNullException(nameof(stream));
        _committedPosition = _innerStream.Position;
        _committedLength = _innerStream.Length;
    }

    public void Deactivate()
    {
        Rollback();
        _innerStream = null!;
        _committedPosition = -1;
        _committedLength = -1;
    }

    public async ValueTask<bool> SyncToNextHeaderAsync(ReadOnlyMemory<byte> magicHeader, long readLimit, CancellationToken ct = default)
    {
        return await StreamSynchronizer.SyncToNextHeaderAsync(_innerStream, magicHeader, readLimit, ct);
    }
    
    public async Task ReadExactlyAsync(Memory<byte> buffer, long readLimit, CancellationToken ct = default)
    {
        if (_innerStream.Position + buffer.Length > readLimit)
            throw new EndOfStreamException();
        
        await _innerStream.ReadExactlyAsync(buffer, ct);
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await _innerStream.WriteAsync(buffer, ct);
    }
    
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _innerStream.FlushAsync(ct);
    }

    public void Commit()
    {
        _committedPosition = _innerStream.Position;
        _committedLength = _innerStream.Length;
    }

    public void Rollback()
    {
        _innerStream.Position = _committedPosition;
        if(_innerStream.CanWrite)
            _innerStream.SetLength(_committedLength);
    }
}