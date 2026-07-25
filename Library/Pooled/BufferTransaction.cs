using System.Buffers;
using HackyMessage.Pooled.Pool;

namespace HackyMessage.Pooled;

public sealed class BufferTransaction<T> : IBufferWriter<T>
{
    private IBufferWriter<T>? _targetWriter = default;
    private ArrayBufferWriterPool<T>.ArrayBufferWriterLease _localBufferLease = default;

    public void Activate(IBufferWriter<T> targetWriter, ArrayBufferWriterPool<T>.ArrayBufferWriterLease localBufferLease)
    {
        _targetWriter = targetWriter ?? throw new ArgumentNullException(nameof(targetWriter));
        _localBufferLease = localBufferLease;
    }

    public void Deactivate()
    {
        Rollback();
        _targetWriter = null;
        _localBufferLease.Dispose(); //returns the local buffer to its pool!
        _localBufferLease = default;
    }

    public void Advance(int count)
        => _localBufferLease.Buffer.Advance(count);

    public Memory<T> GetMemory(int sizeHint = 0)
        => _localBufferLease.Buffer.GetMemory(sizeHint);
    
    public ReadOnlyMemory<T> GetMemory(int start, int length)
        => _localBufferLease.Buffer.WrittenMemory.Slice(start, length);
    
    public Memory<T> GetExactReadBufferMemory(int length)
        => _localBufferLease.Buffer.GetMemory(length).Slice(0, length);
    
    public Span<T> GetSpan(int sizeHint = 0)
        => _localBufferLease.Buffer.GetSpan(sizeHint);
    
    public ReadOnlySpan<T> GetSpan(int start, int length)
        => _localBufferLease.Buffer.WrittenSpan.Slice(start, length);    
    
    public Span<T> GetExactReadBufferSpan(int length)
        => _localBufferLease.Buffer.GetSpan(length).Slice(0, length);
    
    public int GetLength()
        => _localBufferLease.Buffer.WrittenCount;

    public void Commit()
    {
        if (_localBufferLease.Buffer.WrittenCount > 0)
        {
            var targetSpan = _targetWriter!.GetSpan(_localBufferLease.Buffer.WrittenCount);
            _localBufferLease.Buffer.WrittenSpan.CopyTo(targetSpan);
            _targetWriter.Advance(_localBufferLease.Buffer.WrittenCount);
            _localBufferLease.Buffer.Clear();
        }
    }

    public void Rollback()
    {
        _localBufferLease.Buffer.Clear();
    }
}