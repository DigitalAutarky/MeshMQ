using System.Buffers;

namespace HackyMessage.Core.Strategy.Execution;

public sealed class SingleShotExecution<T>(int batchSize, TimeSpan maxDelayInterval) : IExecutionStrategy<T, Envelope<T>>
{
    public int BatchSize { get; } = batchSize;
    public TimeSpan MaxDelayInterval { get; } = maxDelayInterval;
    public ExecutionMode ExecutionMode => ExecutionMode.AtMostOnce;

    //TODO: use dictionary to lookup wrappers instead of iterating over the array
    public async Task ExecuteAsync(ReadOnlyMemory<Envelope<T>> messages, IConsumer<T> consumer, Func<Envelope<T>, Exception, Task> registerFailure)
    {
        //buffers and exceptions relate to input messages by sharing the same index value
        var buffer = ArrayPool<T>.Shared.Rent(messages.Length);
        var exceptions = ArrayPool<Exception?>.Shared.Rent(messages.Length);
        try
        {
            //unwrap messages from envelope into buffer
            InitializeBuffers(messages, buffer, exceptions);

            //process items
            var items = buffer.AsMemory(0, messages.Length);
            await consumer.ConsumeAsync(items,
                (item, exception) => MarkFailed(items, exceptions, item, exception));
        }
        catch (Exception e)
        {
            //mark all items as failed as per single shot idea
            MarkAllFailed(messages, exceptions, e);
        }
        finally{
            //report failures to the caller
            ReportFailedTasks(messages, exceptions, registerFailure);
            
            //return shared buffers
            ArrayPool<T>.Shared.Return(buffer);
            ArrayPool<Exception?>.Shared.Return(exceptions);
        }
    }

    private static void InitializeBuffers(ReadOnlyMemory<Envelope<T>> messages, T[] buffer, Exception?[] exceptions)
    {
        for (var i = 0; i < messages.Length; i++)
        {
            buffer[i] = messages.Span[i].Message;
            exceptions[i] = null;
        }
    }

    private static void MarkFailed(Memory<T> items, Exception?[] exceptions, T item, Exception e)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (!item!.Equals(items.Span[i])) continue;
            exceptions[i] = e;
            break;
        }
    }

    private static void MarkAllFailed(ReadOnlyMemory<Envelope<T>> messages, Exception?[] exceptions, Exception e)
    {
        for (var i = 0; i < messages.Length; i++)
            exceptions[i] = e;
    }
    
    private static void ReportFailedTasks(ReadOnlyMemory<Envelope<T>> messages, Exception?[] exceptions, Func<Envelope<T>, Exception, Task> registerFailure)
    {
        for (var i = 0; i < messages.Length; i++)
            if (exceptions[i] != null)
                registerFailure(messages.Span[i], exceptions[i]!);
    }

    public ValueTask DisposeAsync() 
        => ValueTask.CompletedTask;
}