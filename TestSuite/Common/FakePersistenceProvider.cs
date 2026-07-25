using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Disk;
using HackyMessage.Pooled;

namespace TestSuite.Common;

public class FakePersistenceProvider<T>(IPersistenceProvider<T> innerProvider) : IPersistenceProvider<T>
{
    public readonly ManualResetEventSlim IsUnblocked = new ManualResetEventSlim(true);

    private long _totalEnqueueCount = 0L;
    public long TotalEnqueueCount => Interlocked.Read(ref _totalEnqueueCount);
    
    private int _callsCurrentlyBlocked = 0;
    public int CallsCurrentlyBlocked => Interlocked.CompareExchange(ref _callsCurrentlyBlocked, 0, 0);

    private long _processingConfirmed = 0;
    public long ProcessingConfirmed => Interlocked.CompareExchange(ref _processingConfirmed, 0, 0);

    private long _executionConfirmed = 0;
    public long ExecutionConfirmed => Interlocked.CompareExchange(ref _executionConfirmed, 0, 0);

    public async Task<int> EnqueueAsync(ReadOnlyMemory<WorkItem<T, PersistenceResult>> items, bool isBlocking, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callsCurrentlyBlocked);
        try
        {
            IsUnblocked.Wait(); // deliberately no ct — this is a test artifact, not real backpressure
        }
        finally
        {
            Interlocked.Decrement(ref _callsCurrentlyBlocked);
        }

        Interlocked.Add(ref _totalEnqueueCount, items.Length); // adjust to match however you currently count
        return await innerProvider.EnqueueAsync(items, isBlocking, ct);
    }

    public Task<(int count, long readPosition)> DequeueAsync(T[] buffer, TimeSpan timeout, CancellationToken ct = default)
        => innerProvider.DequeueAsync(buffer, timeout, ct);

    public async ValueTask ConfirmProcessed(long readPosition, CancellationToken ct = default)
    {
        await innerProvider.ConfirmProcessed(readPosition, ct);
        Interlocked.Exchange(ref _processingConfirmed, readPosition);
    }

    public async ValueTask NotifyExecutionComplete(long readPosition, CancellationToken ct = default)
    {
        await innerProvider.NotifyExecutionComplete(readPosition, ct);
        Interlocked.Exchange(ref _executionConfirmed, readPosition);
    }

    public bool IsFull()
        => innerProvider.IsFull();

    public bool IsFullyProcessed()
        => innerProvider.IsFullyProcessed();

    public long WritersAwaitingCapacity()
        => innerProvider.WritersAwaitingCapacity();

    public void Dispose()
        => innerProvider.Dispose();

    public ValueTask DisposeAsync()
        => innerProvider.DisposeAsync();
}