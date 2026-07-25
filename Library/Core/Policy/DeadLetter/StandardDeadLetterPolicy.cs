using HackyMessage.Core.Queue.DeadLetterQueue;

namespace HackyMessage.Core.Policy.DeadLetter;

public class StandardDeadLetterPolicy<T>(long maxCount): IDeadLetterPolicy<T>
{
    public long MaxCount { get; } = maxCount;
    
    public IDeadLetterHandler<T> Create()
        => new DeadLetterHandler<T>(MaxCount);
}