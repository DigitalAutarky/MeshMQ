using System.Buffers;
using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class DisposableArrayPool<T>
{
    private readonly ArrayPool<T> _innerPool = ArrayPool<T>.Shared;

    public DisposableArrayLease Rent(int size)
    {
        var array = _innerPool.Rent(size);
        return new DisposableArrayLease(_innerPool, array, size);
    }
    
    public readonly struct DisposableArrayLease(ArrayPool<T> pool, T[] array, int size)
        : IDisposable, IAsyncDisposable
    {
        public Memory<T> Array { get; } = array.AsMemory()[..size];

        public void Dispose()
        {
            if (array != null)
                pool.Return(array);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}