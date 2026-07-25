using System.Threading.Channels;
using HackyMessage.Common;
using HackyMessage.Core;
using HackyMessage.Core.Strategy;
using HackyMessage.Core.Strategy.Execution;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TestSuite.Common;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ChannelBatcherTest
{
    [Test]
    [Category("Unit")]
    [Description("Writes enough messages to a batched channel to trigger one batch")]
    public async Task ProcessInBatchesAsync_ShouldProcessOneBatch_WhenMaximumBatchSizeIsReached()
    {
        // Setup
        var batchSize = 2;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var channel = Channel.CreateUnbounded <Envelope<string>> ();
        var cancellationToken = CancellationToken.None;
        
        List<List<Envelope<string>>> batches = [];
        var batcher = ChannelBatcher.ProcessInBatchesAsync(
            async (items) => batches.Add([.. items.ToArray()]),
            channel.Reader,
            maxDelayInterval,
            batchSize,
            cancellationToken);
        
        // Act
        var messages = GenerateMessages("one",  "two", "three");
        foreach (var message in messages.ToArray())
            channel.Writer.TryWrite(message);

        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
        
        // Assertions
        Assert.That(batches.Count, Is.EqualTo(1));

        var batch = batches[0];
        var expectedValues = new[] { "one", "two" };
        Assert.That(batch.Select(x => x.Message).SequenceEqual(expectedValues), Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes less messages than the required batch size and verifies a batch is processed due to the timeout")]
    public async Task ProcessInBatchesAsync_ShouldProcessOneBatch_WhenTimeOutIsReached()
    {
        // Setup
        var batchSize = 2;
        var maxDelayInterval = TimeSpan.FromSeconds(1);
        var channel = Channel.CreateUnbounded <Envelope<string>> ();
        var cancellationToken = CancellationToken.None;
        
        List<List<Envelope<string>>> batches = [];
        var batcher = ChannelBatcher.ProcessInBatchesAsync(
            async (items) => batches.Add([.. items.ToArray()]),
            channel.Reader,
            maxDelayInterval,
            batchSize,
            cancellationToken);
        
        // Act
        var messages = GenerateMessages("one");
        foreach (var message in messages.ToArray())
            channel.Writer.TryWrite(message);

        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
        
        // Assertions
        Assert.That(batches.Count, Is.EqualTo(1));

        var batch = batches[0];
        var expectedValues = new[] { "one" };
        Assert.That(batch.Select(x => x.Message).SequenceEqual(expectedValues), Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Ensures that the channel is drained completely when the batcher is cancelled")]
    public async Task ProcessInBatchesAsync_ShouldProcessOneBatch_WhenCancelled()
    {
        // Setup
        var batchSize = 10; //we never reach this
        var maxDelayInterval = Timeout.InfiniteTimeSpan; //we never reach this
        var channel = Channel.CreateUnbounded <Envelope<string>> ();
        var cts = new CancellationTokenSource();
        
        List<List<Envelope<string>>> batches = [];
        var batcher = ChannelBatcher.ProcessInBatchesAsync(
            async (items) => batches.Add([.. items.ToArray()]),
            channel.Reader,
            maxDelayInterval,
            batchSize,
            cts.Token);
        
        // Act
        var messages = GenerateMessages("one",  "two", "three");
        foreach (var message in messages.ToArray())
            channel.Writer.TryWrite(message);
        
        await cts.CancelAsync();
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
        
        // Assertions
        Assert.That(batches.Count, Is.EqualTo(1));

        var batch = batches[0];
        var expectedValues = new[] { "one", "two", "three" };
        Assert.That(batch.Select(x => x.Message).SequenceEqual(expectedValues), Is.True);
    }

    private static ReadOnlyMemory<Envelope<T>> GenerateMessages<T>(params T[] messages)
    {
        var result = new Envelope<T>[messages.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = new Envelope<T>()
            {
                Id = i.ToString(),
                CorrelationId = i.ToString(),
                CreatedAt = DateTime.UtcNow,
                Message = messages[i]
            };
        
        return result.AsMemory();
    }
}