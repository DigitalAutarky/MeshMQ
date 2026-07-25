using DotNext.Threading;
using HackyMessage.Core;
using HackyMessage.Core.Policy;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.Execution;

namespace TestSuite.Common;

public sealed class FakeStringConsumer(
    IChannelPolicy? partitionWritePolicy = null,
    IExecutionPolicy<string>? executionPolicy = null)
    : IConsumer<string>
{
    public IChannelPolicy PartitionWritePolicy
        => partitionWritePolicy ?? new StandardChannelPolicy(4096, TimeSpan.FromSeconds(1));

    public IExecutionPolicy<string> ExecutionPolicy
        => executionPolicy ?? new SingleShotPolicy<string>(100, TimeSpan.FromSeconds(1));
    
    public ManualResetEventSlim IsUnblocked = new ManualResetEventSlim(true);
    public AsyncAutoResetEvent SignalDone { get;  } = new(false);
    
    private readonly Dictionary<string, Exception> _blackList = new();
    
    private long _totalProcessedCount;
    public long TotalProcessedCount => Interlocked.Read(ref _totalProcessedCount);

    public void FailWith(string message, Exception exception)
        =>  _blackList.Add(message, exception);

    public Task ConsumeAsync(ReadOnlyMemory<string> messages, Action<string, Exception> markFailed)
    {
        IsUnblocked.Wait();
        
        foreach (var message in messages.ToArray())
        {
            if (_blackList.TryGetValue(message, out var exception))
            {
                markFailed(message, exception);
                markFailed(message, exception);
            }
        }

        Interlocked.Add(ref _totalProcessedCount, messages.Length);
        SignalDone.Set();
        return Task.CompletedTask;
    }


    public async ValueTask DisposeAsync()
    {
        if (IsUnblocked is IAsyncDisposable isReadyAsyncDisposable)
            await isReadyAsyncDisposable.DisposeAsync();
        else
            IsUnblocked.Dispose();
        await SignalDone.DisposeAsync();
    }
}