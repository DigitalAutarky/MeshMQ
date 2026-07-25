using Microsoft.Extensions.ObjectPool;

namespace HackyMessage.Pooled.Pool;

public sealed class WorkItemPool<TInput, TResult>: IDisposable
{
    private readonly ObjectPool<WorkItem<TInput, TResult>> _innerPool;

    public WorkItemPool(int maxCapacity = 16)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maxCapacity };
        var policy = new WorkItemPolicy();
        _innerPool = provider.Create(policy);
    }
    
    public WorkItemLease Rent(TInput input, ValueTaskCompletionSource<TResult> cs)
    {
        var workItem = _innerPool.Get();
        workItem.Activate(input, cs);
        return new WorkItemLease(_innerPool, workItem);
    }
    
    public void Dispose()
    {
        if (_innerPool is IDisposable disposable)
            disposable.Dispose();
    }
    
    private sealed class WorkItemPolicy : IPooledObjectPolicy<WorkItem<TInput, TResult>>
    {
        public WorkItem<TInput, TResult> Create() => new();

        public bool Return(WorkItem<TInput, TResult> obj)
        {
            obj.Deactivate(); 
            return true; 
        }
    }
    
    public readonly struct WorkItemLease(ObjectPool<WorkItem<TInput, TResult>> pool, WorkItem<TInput, TResult> workItem)
        : IDisposable, IAsyncDisposable
    {
        public WorkItem<TInput, TResult> WorkItem { get; } = workItem;

        public void Dispose()
        {
            if (WorkItem != null)
            {
                pool.Return(WorkItem);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}