using HackyMessage.Core.Strategy;
using HackyMessage.Core.Strategy.Execution;

namespace HackyMessage.Core.Policy.Execution;

public class SingleShotPolicy<T>(int batchSize, TimeSpan maxDelayInterval): IExecutionPolicy<T>
{
    public int BatchSize { get; }  = batchSize;
    public TimeSpan MaxDelayInterval { get; }  = maxDelayInterval;
    
    public IExecutionStrategy<T, Envelope<T>> Create()
        =>  new SingleShotExecution<T>(BatchSize, MaxDelayInterval);
}