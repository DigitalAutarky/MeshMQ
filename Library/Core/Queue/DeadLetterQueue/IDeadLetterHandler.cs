namespace HackyMessage.Core.Queue.DeadLetterQueue;

public interface IDeadLetterHandler<T>
{
    long Count { get;  }
    long MaxCount { get; }
    
    ValueTask<bool> TryAddAsync(Envelope<T> item, Exception exception);
    ValueTask<(bool success, Envelope<T> item, Exception ex)> TryGetAsync(string id);
    ValueTask<bool> TryRemoveAsync(string id);
}