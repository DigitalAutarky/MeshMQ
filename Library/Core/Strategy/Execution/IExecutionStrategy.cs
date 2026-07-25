namespace HackyMessage.Core.Strategy.Execution;

public interface IExecutionStrategy<T, TWrapper>: IAsyncDisposable
{
    int BatchSize { get; }
    TimeSpan MaxDelayInterval { get; }
    ExecutionMode ExecutionMode { get; }

    public Task ExecuteAsync(ReadOnlyMemory<TWrapper> messages, IConsumer<T> consumer, Func<TWrapper, Exception, Task> registerFailure);
}