using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class ValueTaskCompletionSourcePool<T>: IDisposable
{
    private readonly ObjectPool<ValueTaskCompletionSource<T>> _innerPool;

    public ValueTaskCompletionSourcePool(int maxCapacity = 16)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new ValueTaskCompletionSourcePolicy();
        _innerPool = provider.Create(policy);
    }
    
    public ValueTaskCompletionSourceLease<T> Rent()
    {
        var source = _innerPool.Get();
        return new ValueTaskCompletionSourceLease<T>(_innerPool, source);
    }
    
    public void Dispose()
    {
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class ValueTaskCompletionSourcePolicy : IPooledObjectPolicy<ValueTaskCompletionSource<T>>
    {
        public ValueTaskCompletionSource<T> Create() => new();

        public bool Return(ValueTaskCompletionSource<T> source)
        {
            source.Reset();
            return true;
        }
    }
    
    public readonly struct ValueTaskCompletionSourceLease<T>(ObjectPool<ValueTaskCompletionSource<T>> pool, ValueTaskCompletionSource<T> source)
        : IDisposable, IAsyncDisposable
    {
        public ValueTaskCompletionSource<T> Source { get; } = source;

        public void Dispose()
        {
            if (Source != null)
            {
                pool.Return(Source);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}