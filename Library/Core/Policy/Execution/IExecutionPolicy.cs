using HackyMessage.Core.Strategy.Execution;

namespace HackyMessage.Core.Policy.Execution;

public interface IExecutionPolicy<T>
{
    int BatchSize { get; }
    TimeSpan MaxDelayInterval { get; }
    IExecutionStrategy<T, Envelope<T>> Create();
}