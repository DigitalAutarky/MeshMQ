using HackyMessage.Core;
using HackyMessage.Core.Strategy;
using HackyMessage.Core.Strategy.Execution;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TestSuite.Common;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class SingleShotExecutionTest
{
    [Test]
    [Category("Unit")]
    [Description("Executes an array of string with a mocked consumer that reports no failure")]
    public async Task ExecuteAsync_ShouldUseProvidedArguments_WhenCreatingWithValidArguments()
    {
        // Setup
        var batchSize = 3;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var executionStrategy = new SingleShotExecution<string>(batchSize, maxDelayInterval);
        var mockConsumer = Substitute.For<IConsumer<string>>();
        
        // Act
        List<(Envelope<string>, Exception)> failures = [];
        var messages = GenerateMessages("one",  "two", "three");
        await executionStrategy.ExecuteAsync(messages, mockConsumer, async (message, exception) => { failures.Add((message, exception)); });
        
        // Assertions
        Assert.That(executionStrategy.BatchSize, Is.EqualTo(batchSize));
        Assert.That(executionStrategy.MaxDelayInterval, Is.EqualTo(Timeout.InfiniteTimeSpan));
        Assert.That(executionStrategy.ExecutionMode, Is.EqualTo(ExecutionMode.AtMostOnce));
        
        Assert.That(failures.Count, Is.Zero);
        
        var expectedValues = new[] { "one", "two", "three" };
        await mockConsumer.Received().ConsumeAsync(
            messages:Arg.Is<ReadOnlyMemory<string>>(arg => arg.ToArray().SequenceEqual(expectedValues)), 
            markFailed:Arg.Any<Action<string, Exception>>());
    }
    
    [Test]
    [Category("Unit")]
    [Description("Executes an array of string with a mocked consumer that throws an exception")]
    public async Task ExecuteAsync_ShouldFailAllMessages_WhenExeptionIsThrownByTheConsumer()
    {
        // Setup
        var batchSize = 3;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var executionStrategy = new SingleShotExecution<string>(batchSize, maxDelayInterval);
        var mockConsumer = Substitute.For<IConsumer<string>>();
        mockConsumer.ConsumeAsync(Arg.Any<ReadOnlyMemory<string>>(), Arg.Any<Action<string, Exception>>())
            .ThrowsAsync(new InvalidOperationException());
        
        // Act
        List<(Envelope<string>, Exception)> failures = [];
        var messages = GenerateMessages("one",  "two", "three");
        await executionStrategy.ExecuteAsync(messages, mockConsumer, async (message, exception) => { failures.Add((message, exception)); });
        
        // Assertions
        var expectedValues = new[] { "one", "two", "three" };
        Assert.That(failures.Count, Is.EqualTo(3));
        Assert.That(failures.Select(x => x.Item1.Message).SequenceEqual(expectedValues), Is.True);
        Assert.That(failures.Select(x => x.Item2).All((x) => x is InvalidOperationException), Is.True);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Executes an array of string with a faulty consumer that marks specific messages as failure")]
    public async Task ExecuteAsync_ShouldSpecificMessage_WhenConsumerMarksThemAsFailed()
    {
        // Setup
        var batchSize = 3;
        var maxDelayInterval = Timeout.InfiniteTimeSpan;
        var executionStrategy = new SingleShotExecution<string>(batchSize, maxDelayInterval);
        var faultyConsumer = new FakeStringConsumer();
        
        // Act
        List<(Envelope<string>, Exception)> failures = [];
        faultyConsumer.FailWith("faulty", new InvalidOperationException());
        var messages = GenerateMessages("one",  "faulty", "three");
        await executionStrategy.ExecuteAsync(messages, faultyConsumer, async (message, exception) => { failures.Add((message, exception)); });
        
        // Assertions
        var expectedValues = new[] { "faulty" };
        Assert.That(failures.Count, Is.EqualTo(1));
        Assert.That(failures.Select(x => x.Item1.Message).SequenceEqual(expectedValues), Is.True);
        Assert.That(failures.Select(x => x.Item2).All((x) => x is InvalidOperationException), Is.True);
    }

    private static ReadOnlyMemory<HackyMessage.Core.Envelope<T>> GenerateMessages<T>(params T[] messages)
    {
        var result = new HackyMessage.Core.Envelope<T>[messages.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = new HackyMessage.Core.Envelope<T>()
            {
                Id = i.ToString(),
                CorrelationId = i.ToString(),
                CreatedAt = DateTime.UtcNow,
                Message = messages[i]
            };
        
        return result.AsMemory();
    }
}