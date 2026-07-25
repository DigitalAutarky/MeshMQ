using HackyMessage.Persistence.Provider.Disk;
using HackyMessage.Pooled;

namespace HackyMessage.Persistence.Provider;

public interface IPersistenceProvider<T> : IDisposable, IAsyncDisposable
{

    Task<int> EnqueueAsync(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, bool isBlocking = true, CancellationToken ct = default);
    Task<(int count, long readPosition)> DequeueAsync(T[] buffer, TimeSpan timeout, CancellationToken ct = default);
    ValueTask ConfirmProcessed(long readPosition, CancellationToken ct = default);
    ValueTask NotifyExecutionComplete(long readPosition, CancellationToken ct = default);
    public bool IsFull();
    public bool IsFullyProcessed();
    long WritersAwaitingCapacity();

}