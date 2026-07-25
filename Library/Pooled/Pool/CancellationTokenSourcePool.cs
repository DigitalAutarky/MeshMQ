using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class CancellationTokenSourcePool: IDisposable
{
    private readonly ObjectPool<CancellationTokenSource> _innerPool;

    public CancellationTokenSourcePool(int maxCapacity)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new CancellationTokenSourcePolicy();
        _innerPool = provider.Create(policy);
    }
    
    public CancellationTokenSourceLease Rent()
    {
        var source = _innerPool.Get();
        return new CancellationTokenSourceLease(_innerPool, source);
    }
    
    public void Dispose()
    {
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class CancellationTokenSourcePolicy : IPooledObjectPolicy<CancellationTokenSource>
    {
        public CancellationTokenSource Create() => new();

        public bool Return(CancellationTokenSource source)
        {
            if (source.TryReset())
            {
                return true;
            }

            source.Dispose();
            return false;
        }
    }
    
    public readonly struct CancellationTokenSourceLease(ObjectPool<CancellationTokenSource> pool, CancellationTokenSource cts)
        : IDisposable, IAsyncDisposable
    {
        public CancellationTokenSource Cts { get; } = cts;

        public void Dispose()
        {
            if (Cts != null)
            {
                pool.Return(Cts);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}