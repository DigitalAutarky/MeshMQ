using System.Threading.Channels;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Metric;
using HackyMessage.Serialization;
using HackyMessage.Serialization.Serializers;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class StandardChannelPolicyTest
{
    [Test]
    [Category("Unit")]
    [Description("Creates a channel using its policy and verifies the channels arguments/properties")]
    public void Create_ShouldUseProvidedArguments_WhenCreatingWithValidArguments()
    {
        // Setup
        var bufferSize = 123;
        var maxDelayInterval = TimeSpan.FromMilliseconds(123);
        var policy = new StandardChannelPolicy(bufferSize, maxDelayInterval);
        var channel = policy.Create<string>(true, true);
        
        // Assertions
        Assert.That(policy.BufferSize, Is.EqualTo(bufferSize));
        Assert.That(policy.MaxDelayInterval, Is.EqualTo(maxDelayInterval));
        Assert.That(channel.Options.Capacity, Is.EqualTo(bufferSize));
        Assert.That(channel.Options.FullMode, Is.EqualTo(BoundedChannelFullMode.Wait));
        Assert.That(channel.Options.AllowSynchronousContinuations, Is.False);
        Assert.That(channel.Options.SingleReader, Is.True);
        Assert.That(channel.Options.SingleWriter, Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies the behaviour when the created channel is full (non-blocking)")]
    public void Create_ShouldRejectWrites_WhenTheChannelBufferIsFull()
    {
        // Setup
        const int bufferSize = 2;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var policy = new StandardChannelPolicy(bufferSize, maxDelayInterval);
        var channel = policy.Create<string>(true, true);
        
        // 1. Act
        var one = channel.Channel.Writer.TryWrite("one");
        var two = channel.Channel.Writer.TryWrite("two");
        var three = channel.Channel.Writer.TryWrite("Three");
        
        // 1. Assertions (only the first two writes should have succeeded)
        Assert.That(channel.Channel.Reader.Count, Is.EqualTo(2));
        Assert.That(one, Is.True);
        Assert.That(two, Is.True);
        Assert.That(three, Is.False);
        
        // 2. Act remove one of the stored messages to unblock the writer
        var hasRetrievedItem = channel.Channel.Reader.TryRead(out var item);
        
        // 2. Assertions (now we should have only one element left in thannel
        // so we can store another element later
        Assert.That(hasRetrievedItem, Is.True);
        Assert.That(item, Is.EqualTo("one"));
        Assert.That(channel.Channel.Reader.Count, Is.EqualTo(1));

        // 3. Try to write the third element again and assert success
        three = channel.Channel.Writer.TryWrite("Three");
        Assert.That(three, Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies the behaviour when the created channel is full (blocking)")]
    public async Task Create_ShouldWait_WhenTheChannelBufferIsFull()
    {
        // Setup
        const int bufferSize = 2;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var policy = new StandardChannelPolicy(bufferSize, maxDelayInterval);
        var channel = policy.Create<string>(true, true);
        
        // 1. Act
        await channel.Channel.Writer.WriteAsync("one");
        await channel.Channel.Writer.WriteAsync("two");
        var task = Task.Run(async () => await channel.Channel.Writer.WriteAsync("three"));
        await Task.Delay(1000);
        
        // 1. Assertions (only the first two writes should have succeeded)
        Assert.That(channel.Channel.Reader.Count, Is.EqualTo(2));
        Assert.That(task.IsCompletedSuccessfully, Is.False);
        
        // 2. Act remove one of the stored messages to unblock the writer
        var item1 = await channel.Channel.Reader.ReadAsync();
        await Task.Delay(1000);
        
        // 2. Assertions (now we should still have elements left in thannel
        // because the third item should have been written after we removed
        // one of the written items
        Assert.That(item1, Is.EqualTo("one"));
        Assert.That(channel.Channel.Reader.Count, Is.EqualTo(2));
        Assert.That(task.IsCompletedSuccessfully, Is.True);
        
        // 3. Read all elements left and verify them
        var item2 = await channel.Channel.Reader.ReadAsync();
        Assert.That(item2, Is.EqualTo("two"));
        
        var item3 = await channel.Channel.Reader.ReadAsync();
        Assert.That(item3, Is.EqualTo("three"));
    }
}