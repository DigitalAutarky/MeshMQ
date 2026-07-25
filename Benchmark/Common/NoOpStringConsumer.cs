using DotNext.Threading;
using HackyMessage.Core;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.Execution;

namespace Benchmark.Common;

public sealed class NoOpStringConsumer(
    IChannelPolicy? partitionWritePolicy = null,
    IExecutionPolicy<string>? executionPolicy = null,
    int doneAfterCount = 0)
    : IConsumer<string>
{
    public IChannelPolicy PartitionWritePolicy
        => partitionWritePolicy ?? new StandardChannelPolicy(4096, TimeSpan.FromSeconds(1));

    public IExecutionPolicy<string> ExecutionPolicy
        => executionPolicy ?? new SingleShotPolicy<string>(100, TimeSpan.FromSeconds(1));

    private long _totalProcessedCount = 0L;
    public AsyncAutoResetEvent SignalDone { get;  } = new(false);

    public Task ConsumeAsync(ReadOnlyMemory<string> messages, Action<string, Exception> markFailed)
    {
        Interlocked.Add(ref _totalProcessedCount, messages.Length);
        
        if(Interlocked.Read(ref _totalProcessedCount) >= doneAfterCount)
            SignalDone.Set();
        
        return Task.CompletedTask;
    }
    
    public async ValueTask DisposeAsync()
        => await SignalDone.DisposeAsync();
}