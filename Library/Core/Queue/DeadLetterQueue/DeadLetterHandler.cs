using DotNext.Collections.Generic;
using HackyMessage.Common;

namespace HackyMessage.Core.Queue.DeadLetterQueue;

public class DeadLetterHandler<T>(long maxCount) : IDeadLetterHandler<T>
{
    private long _count = 0;
    public long Count => Interlocked.Read(ref _count);
    
    private long _maxCount = maxCount;
    public long MaxCount => Interlocked.Read(ref _maxCount);

    private readonly Dictionary<string, (Envelope<T> Item, Exception ex)> _failedItems = new((int)maxCount);
    private readonly MyAsyncLock _lock = new();
    
    public async ValueTask<bool> TryAddAsync(Envelope<T> item, Exception exception)
    {
        using var rwLock = await _lock.AcquireAsync(Timeout.InfiniteTimeSpan);
        if (Count >= MaxCount) return false;
        
        _failedItems[item.Id] = (item, exception);
        Interlocked.Increment(ref _count);
        return true;
    }

    public async ValueTask<(bool success, Envelope<T> item, Exception ex)> TryGetAsync(string id)
    {
        using var rwLock = await _lock.AcquireAsync(Timeout.InfiniteTimeSpan);
        var success  = _failedItems.TryGetValue(id, out var item);
        return (success, item.Item, item.ex);
    }

    public async ValueTask<bool> TryRemoveAsync(string id)
    {
        using var rwLock = await _lock.AcquireAsync(Timeout.InfiniteTimeSpan);
        var result = _failedItems.TryRemove(id);
        return result.HasValue;
    }
}