using System.Buffers;
using HackyMessage.Metric;
using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class ArrayBufferWriterPool<T>: IDisposable
{
    private readonly ObjectPool<ArrayBufferWriter<T>> _innerPool;

    public ArrayBufferWriterPool(int maxCapacity = 16, int initialWriterCapacity = 256,  bool removeOutliers = false)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new ArrayBufferWriterPolicy(initialWriterCapacity, removeOutliers);
        _innerPool = provider.Create(policy);
    }
    
    public ArrayBufferWriterLease Rent()
    {
        var writer = _innerPool.Get();
        return new ArrayBufferWriterLease(_innerPool, writer);
    }
    
    public void Dispose()
    {
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class ArrayBufferWriterPolicy(int initialCapacity, bool removeOutliers) : IPooledObjectPolicy<ArrayBufferWriter<T>>
    {
        private readonly SlidingIqrOutlierDetector _outlierDetector = new(128, 64);
        public ArrayBufferWriter<T> Create() => new(initialCapacity);

        public bool Return(ArrayBufferWriter<T> obj)
        {
            obj.Clear();
            
            //always return to pool if outlier detection is disabled
            if (!removeOutliers)
                return true;
            
            //remove outliers to prevent memory bloat
            if (_outlierDetector.IsBigOutlier(obj.Capacity))
                return false;
            
            //observe capacity so it learns what standard capacities look like
            _outlierDetector.Observe(obj.Capacity);
            return true;
        }
    }
    
    public readonly struct ArrayBufferWriterLease(ObjectPool<ArrayBufferWriter<T>> pool, ArrayBufferWriter<T> buffer)
        : IDisposable, IAsyncDisposable
    {
        public ArrayBufferWriter<T> Buffer { get; } = buffer;

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