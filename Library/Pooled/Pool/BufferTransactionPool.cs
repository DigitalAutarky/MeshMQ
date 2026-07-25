using System.Buffers;
using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class BufferTransactionPool<T>: IDisposable
{
    private readonly ArrayBufferWriterPool<T> bufferPool;
    private readonly ObjectPool<BufferTransaction<T>> _innerPool;

    public BufferTransactionPool(int maxCapacity = 16, bool removeOutliers = false)
    {
        bufferPool = new ArrayBufferWriterPool<T>(maxCapacity, 256, removeOutliers);
        
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new BufferTransactionPolicy();
        _innerPool = provider.Create(policy);
    }
    
    public BufferTransactionLease Rent(ArrayBufferWriter<T> targetBuffer)
    {
        var transaction = _innerPool.Get();
        var localBufferLease = bufferPool.Rent();
        transaction.Activate(targetBuffer, localBufferLease);
        return new BufferTransactionLease(_innerPool, transaction);
    }
    
    public void Dispose()
    {
        bufferPool.Dispose();
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class BufferTransactionPolicy : IPooledObjectPolicy<BufferTransaction<T>>
    {
        public BufferTransaction<T> Create() => new();

        public bool Return(BufferTransaction<T> obj)
        {
            obj.Deactivate(); 
            return true; 
        }
    }
    
    public readonly struct BufferTransactionLease(ObjectPool<BufferTransaction<T>> pool, BufferTransaction<T> buffer)
        : IDisposable, IAsyncDisposable
    {
        public BufferTransaction<T> Buffer { get; } = buffer;

        public void Dispose()
        {
            if (Buffer != null)
                pool.Return(Buffer);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}