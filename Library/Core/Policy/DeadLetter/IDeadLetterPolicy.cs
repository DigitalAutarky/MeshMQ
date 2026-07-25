using HackyMessage.Core.Queue.DeadLetterQueue;
using HackyMessage.Core.Strategy;

namespace HackyMessage.Core.Policy.DeadLetter;

public interface IDeadLetterPolicy<T>
{
    long MaxCount { get; }
    IDeadLetterHandler<T> Create();
}