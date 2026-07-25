using HackyMessage.Core.Policy;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.DeadLetter;
using HackyMessage.Core.Policy.Execution;

namespace HackyMessage.Core;

public interface IConsumer<T>: IAsyncDisposable
{
    public IChannelPolicy PartitionWritePolicy
        => new StandardChannelPolicy(4096, TimeSpan.FromSeconds(1));

    public IExecutionPolicy<T> ExecutionPolicy
        => new SingleShotPolicy<T>(100, TimeSpan.FromSeconds(1));

    public IDeadLetterPolicy<T> DeadLetterPolicy
        => new StandardDeadLetterPolicy<T>(100);
    
    Task ConsumeAsync(ReadOnlyMemory<T>  messages, Action<T, Exception> markFailed);
}