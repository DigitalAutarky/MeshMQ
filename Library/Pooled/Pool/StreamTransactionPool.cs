using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class StreamTransactionPool: IDisposable
{
    private readonly ObjectPool<StreamTransaction> _innerPool;

    public StreamTransactionPool(int maxCapacity = 16)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new StreamTransactionPolicy();
        _innerPool = provider.Create(policy);
    }
    
    public StreamTransactionLease Rent(Stream stream)
    {
        var transaction = _innerPool.Get();
        transaction.Activate(stream);
        return new StreamTransactionLease(_innerPool, transaction);
    }
    
    public void Dispose()
    {
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class StreamTransactionPolicy : IPooledObjectPolicy<StreamTransaction>
    {
        public StreamTransaction Create() => new();

        public bool Return(StreamTransaction obj)
        {
            obj.Deactivate(); 
            return true; 
        }
    }
    
    public readonly struct StreamTransactionLease(ObjectPool<StreamTransaction> pool, StreamTransaction stream)
        : IDisposable, IAsyncDisposable
    {
        public StreamTransaction Stream { get; } = stream;

        public void Dispose()
        {
            if (Stream != null)
            {
                pool.Return(Stream);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}