using HackyMessage.Core;
using HackyMessage.Core.Partition;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.DeadLetter;
using HackyMessage.Core.Policy.Execution;
using HackyMessage.Core.Queue.DeadLetterQueue;
using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Factory;
using NSubstitute;
using TestSuite.Common;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class PartitionTests
{
    [Test]
    [Category("Integration")]
    [Description("Submits one message to the partition (blocking mode) and verifies that it reaches the consumer eventually")]
    public async Task EndToEnd_MessageShouldBeDevileredToConsumer_WhenUsingBlockingMode()
    {
        // 1. Setup resources
        await using var logFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var realWal = await GenerateRealWriteAheasLog<string>(logFile.FileName, indexFile.FileName);
        var (mockConsumer, deliveryTcs) = GenerateMockConsumer<string>();
        var deadLetterHandler = new StandardDeadLetterPolicy<string>(100).Create();
        await using var partition = new Partition<string>(realWal, mockConsumer, deadLetterHandler);
        
        // 2. Act: Append the message into the partition pipeline
        var testMessage = GenerateTestMessage<string>("id123", "test message");
        var acceptResult = await partition.AcceptAsync(testMessage);

        // 3. Assert Part A: Verify the frontend API acknowledged successful persistence
        Assert.That(acceptResult, Is.EqualTo(CachedAcceptResult.Success));

        // 4. Assert Part B: Await the background processing loop. 
        // This proves the message was written to disk, read back out via DequeueAsync, and handed to the consumer.
        var combinedTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using (combinedTimeoutCts.Token.Register(() => deliveryTcs.TrySetCanceled()))
        {
            // DeliveryTcs will return messages passed to consumer
            string[] receivedMessages = await deliveryTcs.Task;
            Assert.That(receivedMessages.Length, Is.EqualTo(1));
            Assert.That(receivedMessages[0], Is.EqualTo("test message"));
        }
    }
    
    [Test]
    [Category("Integration")]
    [Description("Submits one message to the partition (non-blocking mode) and verifies that it reaches the consumer eventually")]
    public async Task EndToEnd_MessageShouldBeDevileredToConsumer_WhenUsingNonBlockingMode()
    {
        // 1. Setup resources
        await using var logFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var realWal = await GenerateRealWriteAheasLog<string>(logFile.FileName, indexFile.FileName);
        var (mockConsumer, deliveryTcs) = GenerateMockConsumer<string>();
        var deadLetterHandler = new StandardDeadLetterPolicy<string>(100).Create();
        await using var partition = new Partition<string>(realWal, mockConsumer, deadLetterHandler);
        
        // 2. Act: Append the message into the partition pipeline
        var testMessage = GenerateTestMessage<string>("id123","test message");
        var acceptResult = await partition.TryAcceptAsync(testMessage);

        // 3. Assert Part A: Verify the frontend API acknowledged successful persistence
        Assert.That(acceptResult, Is.EqualTo(CachedAcceptResult.Success));

        // 4. Assert Part B: Await the background processing loop. 
        // This proves the message was written to disk, read back out via DequeueAsync, and handed to the consumer.
        var combinedTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using (combinedTimeoutCts.Token.Register(() => deliveryTcs.TrySetCanceled()))
        {
            // DeliveryTcs will return messages passed to consumer
            string[] receivedMessages = await deliveryTcs.Task;
            Assert.That(receivedMessages.Length, Is.EqualTo(1));
            Assert.That(receivedMessages[0], Is.EqualTo("test message"));
        }
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that a message which fails in the consumer is reported using the provided callback")]
    public async Task AcceptAsync_MessageShouldBeReported_WhenFailingDuringConsumerProcessing()
    {
        // Setup persistence
        await using var logFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var realWal = await GenerateRealWriteAheasLog<string>(logFile.FileName, indexFile.FileName);
        
        // Setup consumer
        var expectedMessage = "test-message";
        await using var fakeConsumer = new FakeStringConsumer();
        fakeConsumer.FailWith(expectedMessage, new Exception());
        
        // Setup dead letter queue/handler
        var deadLetterHandler = new StandardDeadLetterPolicy<string>(100).Create();
        
        // Finally setup the partition
        await using var partition = new Partition<string>(realWal, fakeConsumer, deadLetterHandler);
        
        // Submit a message
        var expectedId = "id123";
        var testMessage = GenerateTestMessage(expectedId, expectedMessage);
        var acceptResult = await partition.AcceptAsync(testMessage);
        
        // Wait and poll for completion (takes a bit of time for the message to reach the dead letter queue)
        var consumed = await fakeConsumer.SignalDone.WaitAsync(TimeSpan.FromSeconds(5));
        var (success, item, exception) = await PollDeadLetterQueue(deadLetterHandler, expectedId);
        
        // Assertions
        Assert.That(consumed, Is.True);
        Assert.That(acceptResult, Is.EqualTo(CachedAcceptResult.Success));
        Assert.That(success, Is.True);
        Assert.That(deadLetterHandler.Count, Is.EqualTo(1));
        Assert.That(item.Id, Is.EqualTo(expectedId));
        Assert.That(item.Message, Is.EqualTo(expectedMessage));
        Assert.That(exception, Is.TypeOf<Exception>());
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that a message which fails in the consumer is reported using the provided callback")]
    public async Task TryAcceptAsync_MessageShouldBeReported_WhenFailingDuringConsumerProcessing()
    {
        // Setup persistence
        await using var logFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var realWal = await GenerateRealWriteAheasLog<string>(logFile.FileName, indexFile.FileName);
        
        // Setup consumer
        var expectedMessage = "test-message";
        await using var faultyConsumer = new FakeStringConsumer();
        faultyConsumer.FailWith(expectedMessage, new Exception());
        
        // Setup dead letter queue/handler
        var deadLetterHandler = new StandardDeadLetterPolicy<string>(100).Create();
        
        // Finally setup the partition
        await using var partition = new Partition<string>(realWal, faultyConsumer, deadLetterHandler);
        
        // Submit a message
        var expectedId = "id123";
        var testMessage = GenerateTestMessage(expectedId, expectedMessage);
        var acceptResult = await partition.TryAcceptAsync(testMessage);
        
        // Wait and poll for completion (takes a bit of time for the message to reach the dead letter queue)
        var consumed = await faultyConsumer.SignalDone.WaitAsync(TimeSpan.FromSeconds(5));
        var (success, item, exception) = await PollDeadLetterQueue(deadLetterHandler, expectedId);
        
        // Assertions
        Assert.That(consumed, Is.True);
        Assert.That(acceptResult, Is.EqualTo(CachedAcceptResult.Success));
        Assert.That(success, Is.True);
        Assert.That(deadLetterHandler.Count, Is.EqualTo(1));
        Assert.That(item.Id, Is.EqualTo(expectedId));
        Assert.That(item.Message, Is.EqualTo(expectedMessage));
        Assert.That(exception, Is.TypeOf<Exception>());
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that shutdown cancels all pending blocking-mode items uniformly — " +
              "whether they were already shipped into the WAL, sitting in the channel buffer, " +
              "or still blocked trying to write into the channel")]
    public async Task DisposeAsync_BlockingMode_CancelsAllPendingItemsUniformly()
    {
        // A) Arrange
        const int messageCount = 25;
        const int bufferSize = 10;

        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();

        var writePolicy = new StandardChannelPolicy(maxDelayInterval: Timeout.InfiniteTimeSpan, bufferSize: bufferSize);
        var executionPolicy = new SingleShotPolicy<string>(messageCount + 1, Timeout.InfiniteTimeSpan);
        var (persistence, partition) = await GeneratePartition(dataFile.FileName, indexFile.FileName, writePolicy, executionPolicy);

        persistence.IsUnblocked.Reset();

        // B) Act — three phases, each verified before the next begins, so the final
        // pipeline shape (1 shipped batch / 1 full channel / N blocked writers) is
        // guaranteed by construction rather than inferred from timing.

        // Phase 1: fill exactly one batch's worth. The channel starts empty and nothing
        // else is competing for it, so the batcher deterministically picks up all 10 and
        // ships them into EnqueueAsync, where they park on IsUnblocked.
        var phase1Tasks = SubmitTestMessagesBlockingMode(partition, bufferSize);

        await TestHelpers.WaitUntilAsync(
            () => persistence.CallsCurrentlyBlocked == 1 && partition.InMemoryBufferCount == 0,
            TimeSpan.FromSeconds(5),
            $"Expected first batch shipped and channel drained, got " +
            $"CallsCurrentlyBlocked={persistence.CallsCurrentlyBlocked}, " +
            $"InMemoryBufferCount={partition.InMemoryBufferCount}");

        // Phase 2: the reader is now permanently parked inside EnqueueAsync and isn't
        // touching the channel anymore, so this batch fills it to capacity deterministically.
        var phase2Tasks = SubmitTestMessagesBlockingMode(partition, bufferSize);

        await TestHelpers.WaitUntilAsync(
            () => partition.InMemoryBufferCount == bufferSize,
            TimeSpan.FromSeconds(5),
            $"Expected channel filled to capacity, got InMemoryBufferCount={partition.InMemoryBufferCount}");

        // Phase 3: the channel is full and nothing is draining it, so these calls block
        // directly on the channel's own WriteAsync — no waiting needed, this is guaranteed
        // synchronously by the bounded channel's Wait mode.
        var phase3Tasks = SubmitTestMessagesBlockingMode(partition, messageCount - 2 * bufferSize);

        var acceptTasks = phase1Tasks.Concat(phase2Tasks).Concat(phase3Tasks).ToList();
        Assert.That(acceptTasks, Has.Count.EqualTo(messageCount));
        Assert.That(acceptTasks.Count(t => t.IsCompleted), Is.EqualTo(0),
            "No blocking write should complete before dispose is triggered");

        // Pull the plug. Cancellation is now uniform, so there's no ordering race between
        // CancelAsync and unblocking persistence — every item still waiting anywhere in
        // the pipeline is abandoned once the token is cancelled, regardless of when Set()
        // happens to run relative to it.
        var disposalTask = partition.DisposeAsync();
        persistence.IsUnblocked.Set();
        await disposalTask;

        // C) Assertions
        Assert.That(partition.InMemoryBufferCount, Is.EqualTo(0));

        // 20 items reach the WAL wrapper at all: the 10 from phase 1 (already shipped,
        // just waiting on IsUnblocked) plus the 10 from phase 2 (swept out of the channel
        // during the post-cancellation drain). Both groups carry the already-cancelled
        // token by the time they're actually processed, so reaching the WAL no longer
        // implies success — it just means EnqueueAsync was called.
        Assert.That(persistence.TotalEnqueueCount, Is.EqualTo(2 * bufferSize));

        // Every item — the 20 that reached the WAL and got cancelled there, and the 5
        // that never left the channel-write boundary — resolves as Cancelled uniformly.
        await AssertStateAsync(CachedAcceptResult.Cancelled, acceptTasks, messageCount);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that shutdown cancels all pending non-blocking-mode items uniformly, " +
                  "while items already rejected by TryWrite at submission time are unaffected")]
    public async Task DisposeAsync_NonBlockingMode_CancelsAllPendingItemsUniformly()
    {
        // A) Arrange
        const int messageCount = 25;
        const int bufferSize = 10;

        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();

        var writePolicy = new StandardChannelPolicy(maxDelayInterval: Timeout.InfiniteTimeSpan, bufferSize: bufferSize);
        var executionPolicy = new SingleShotPolicy<string>(messageCount + 1, Timeout.InfiniteTimeSpan);
        var (persistence, partition) = await GeneratePartition(dataFile.FileName, indexFile.FileName, writePolicy, executionPolicy);

        persistence.IsUnblocked.Reset();

        // B) Act — same three-phase structure as the blocking test.

        // Phase 1: channel starts empty, nothing competing — all 10 TryWrites succeed
        // deterministically and the batcher ships them into EnqueueAsync.
        var phase1Tasks = SubmitTestMessagesNonBlockingMode(partition, bufferSize);

        await TestHelpers.WaitUntilAsync(
            () => persistence.CallsCurrentlyBlocked == 1 && partition.InMemoryBufferCount == 0,
            TimeSpan.FromSeconds(5),
            $"Expected first batch shipped and channel drained, got " +
            $"CallsCurrentlyBlocked={persistence.CallsCurrentlyBlocked}, " +
            $"InMemoryBufferCount={partition.InMemoryBufferCount}");

        // Phase 2: reader is parked inside EnqueueAsync and isn't touching the channel,
        // so these 10 TryWrites succeed deterministically and fill it to capacity.
        var phase2Tasks = SubmitTestMessagesNonBlockingMode(partition, bufferSize);

        await TestHelpers.WaitUntilAsync(
            () => partition.InMemoryBufferCount == bufferSize,
            TimeSpan.FromSeconds(5),
            $"Expected channel filled to capacity, got InMemoryBufferCount={partition.InMemoryBufferCount}");

        // Phase 3: channel is full and nothing is draining it, so TryWrite fails
        // synchronously for all 5 — resolved immediately as RetryLater, no waiting needed.
        var phase3Tasks = SubmitTestMessagesNonBlockingMode(partition, messageCount - 2 * bufferSize);

        var acceptTasks = phase1Tasks.Concat(phase2Tasks).Concat(phase3Tasks).ToList();
        Assert.That(acceptTasks, Has.Count.EqualTo(messageCount));

        // Pull the plug — same reasoning as the blocking test: uniform cancellation
        // removes the ordering race between CancelAsync and Set().
        var disposalTask = partition.DisposeAsync();
        persistence.IsUnblocked.Set();
        await disposalTask;

        // C) Assertions
        Assert.That(partition.InMemoryBufferCount, Is.Zero);

        // Only the 20 items from phases 1 and 2 ever reach the WAL wrapper; phase 3
        // never got past TryWrite.
        Assert.That(persistence.TotalEnqueueCount, Is.EqualTo(2 * bufferSize));

        // Phase 3's rejections were locked in synchronously at submission time, before
        // dispose was even called — unaffected by shutdown.
        await AssertStateAsync(CachedAcceptResult.RetryLater, acceptTasks, messageCount - 2 * bufferSize);

        // Phases 1 and 2 both carry the already-cancelled token by the time EnqueueAsync
        // actually processes them, so both resolve Cancelled uniformly.
        await AssertStateAsync(CachedAcceptResult.Cancelled, acceptTasks, 2 * bufferSize);
    }

    [Test]
    [Category("Integration")]
    [Description("Verifies that partial processing batches are retrieved and processed")]
    public async Task DisposeAsync_WhenItemsAreBatchedForExecution_ProcessesPartialBatchBeforeStoppingToAvoidDataLoss()
    {
        // A) Arrange
        const int messageCount = 10; //slightly less than the execution batch size so messages will wait in the retrieval buffer
        
        // Set a massive timeout interval so the batcher won't naturally flush 
        // while our test loop is running
        var writePolicy = new StandardChannelPolicy(maxDelayInterval: Timeout.InfiniteTimeSpan, bufferSize: 10);
        var executionPolicy = new SingleShotPolicy<string>((2*messageCount)+1, Timeout.InfiniteTimeSpan); //These conditions will never be met
        var fakeConsumer = new FakeStringConsumer(writePolicy, executionPolicy);
        
        // We dont need the dead letter handler so we just create a mock that does nothing
        var mockDeadLetterHandler = Substitute.For<IDeadLetterHandler<string>>();
        
        // Create the persistence provider
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        var persistenceProvider = await GenerateRealWriteAheasLog<string>(dataFile.FileName, indexFile.FileName);
        var wrappedPersistenceProvider = new FakePersistenceProvider<Envelope<string>>(persistenceProvider);
        
        // Finally we can create our partition to test it
        var partition = new Partition<string>(wrappedPersistenceProvider, fakeConsumer, mockDeadLetterHandler);

        // B) Act
        // 1. Submit items to the partition
        var acceptTasks = SubmitTestMessagesEachMode(partition, messageCount);
        await AssertStateAsync(CachedAcceptResult.Success, acceptTasks, acceptTasks.Count);
        
        // 2. Wait a bit to give the read process some time to read items into the execution buffer
        // and then pull the plug.
        await Task.Delay(1000);
        await partition.DisposeAsync();
        
        // C) Assertions
        // Verify everything was persisted and processed.
        Assert.AreEqual(20, wrappedPersistenceProvider.TotalEnqueueCount);
        Assert.AreEqual(20, fakeConsumer.TotalProcessedCount);
        Assert.AreEqual(0, partition.InMemoryBufferCount);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that messages are submitted and processed when timeout is reachde before batch batch is full")]
    public async Task EndToEnd_MessagesShouldBeProcessed_WhenTimeOutsAreReached()
    {
        int messageCount = 10;
        
        // Setup persistence
        await using var logFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var realWal = await GenerateRealWriteAheasLog<string>(logFile.FileName, indexFile.FileName);
        
        // Setup consumer
        var writePolicy = new StandardChannelPolicy(maxDelayInterval: TimeSpan.FromSeconds(1), bufferSize: messageCount+1);
        var executionPolicy = new SingleShotPolicy<string>((2*messageCount)+1, TimeSpan.FromSeconds(1));
        await using var fakeConsumer = new FakeStringConsumer(writePolicy, executionPolicy);
        
        // Setup dead letter queue/handler
        var deadLetterHandler = new StandardDeadLetterPolicy<string>(100).Create();
        
        // Finally setup the partition
        await using var partition = new Partition<string>(realWal, fakeConsumer, deadLetterHandler);
        
        // Submit messages
        var acceptResults = SubmitTestMessagesEachMode(partition, messageCount);
        
        // Wait until the consumer has processed a batch
        var consumed = await fakeConsumer.SignalDone.WaitAsync(TimeSpan.FromSeconds(5));

        // Assertions
        Assert.That(consumed, Is.True);
        Assert.AreEqual(fakeConsumer.TotalProcessedCount, 2*messageCount);
        Assert.That(deadLetterHandler.Count, Is.Zero);
        await AssertStateAsync(CachedAcceptResult.Success, acceptResults, 2*messageCount);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Verifies that messages are processed and the persistence provider is updated with processing state (items read & items executed)")]
    public async Task EndToEnd_ShouldUpdatePersistenceState_WhenProcessingItemsWithAtMostOnceDelivery()
    {
        // Setup persistence
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();

        var writePolicy = new StandardChannelPolicy(maxDelayInterval: Timeout.InfiniteTimeSpan, bufferSize: 1);
        var executionPolicy = new SingleShotPolicy<string>(1, Timeout.InfiniteTimeSpan);
        var (persistence, partition) = await GeneratePartition(dataFile.FileName, indexFile.FileName, writePolicy, executionPolicy);

        // Act
        var results = SubmitTestMessagesEachMode(partition, 1);
        await Task.Delay(1000); // Give it some time to process

        // Assertions
        await AssertStateAsync(CachedAcceptResult.Success, results, 2);
        Assert.That(persistence.ProcessingConfirmed, Is.GreaterThan(0));
        Assert.That(persistence.ExecutionConfirmed, Is.GreaterThan(0));
        Assert.AreEqual(persistence.ProcessingConfirmed,  persistence.ExecutionConfirmed);
    }
    
    private static async Task<(FakePersistenceProvider<Envelope<string>> persistence, Partition<string> partition)> 
        GeneratePartition(string dataFileName, string indexFileName, StandardChannelPolicy writePolicy, SingleShotPolicy<string> executionPolicy)
    {
        var mockConsumer = Substitute.For<IConsumer<string>>();
        mockConsumer.PartitionWritePolicy.Returns(writePolicy);
        mockConsumer.ExecutionPolicy.Returns(executionPolicy);
        
        var mockDeadLetterHandler = Substitute.For<IDeadLetterHandler<string>>();
        var persistenceProvider = await GenerateRealWriteAheasLog<string>(dataFileName, indexFileName);
        var wrappedPersistenceProvider = new FakePersistenceProvider<Envelope<string>>(persistenceProvider);
        
        var partition = new Partition<string>(wrappedPersistenceProvider, mockConsumer, mockDeadLetterHandler);
        return (wrappedPersistenceProvider, partition);
    }

    private static async Task AssertStateAsync(AcceptResult state, List<Task<AcceptResult>> tasks, int count)
    {
        var completedResults = new List<AcceptResult>();
        foreach (var task in tasks)
        {
            // Safely yield control until the background worker completes this item
            var result = await task; 
            completedResults.Add(result);
        }
        var counted = completedResults.Count(r => r.Equals(state));
        Assert.AreEqual(count, counted);
    }

    private static List<Task<AcceptResult>> SubmitTestMessagesEachMode(Partition<string> partition, int count)
    {
        var acceptTasks = new List<Task<AcceptResult>>();
        for (var i = 0; i < count; i++)
        {
            var envelope = GenerateTestMessage($"Id {i}", $"Msg {i}");
            acceptTasks.Add(partition.AcceptAsync(envelope).AsTask());
            acceptTasks.Add(partition.TryAcceptAsync(envelope).AsTask());
        }

        return acceptTasks;
    }
    
    private static List<Task<AcceptResult>> SubmitTestMessagesBlockingMode(Partition<string> partition, int count)
    {
        var acceptTasks = new List<Task<AcceptResult>>();
        for (var i = 0; i < count; i++)
        {
            var envelope = GenerateTestMessage($"Id {i}", $"Msg {i}");
            acceptTasks.Add(partition.AcceptAsync(envelope).AsTask());
        }

        return acceptTasks;
    }
    
    private static List<Task<AcceptResult>> SubmitTestMessagesNonBlockingMode(Partition<string> partition, int count)
    {
        var acceptTasks = new List<Task<AcceptResult>>();
        for (var i = 0; i < count; i++)
        {
            var envelope = GenerateTestMessage($"Id {i}", $"Msg {i}");
            acceptTasks.Add(partition.TryAcceptAsync(envelope).AsTask());
        }

        return acceptTasks;
    }

    private static Envelope<T> GenerateTestMessage<T>(string id, T message)
    {
        return new Envelope<T>
        {
            Id = id,
            CorrelationId = "123",
            CreatedAt = DateTime.UtcNow,
            Message = message
        };
    }

    private static async Task<IPersistenceProvider<Envelope<T>>>
        GenerateRealWriteAheasLog<T>(string logFile, string indexFile, int highWatermark = 1024*1024, int lowWatermark = 512*512)
    {
        return await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<T>>(
                topic: "integration-tests",
                queue: "test-queue",
                partition: 0,
                highWatermark: highWatermark,
                lowWatermark: lowWatermark,
                maxSize: 1024 * 1024,
                fileName: logFile,
                indexName: indexFile
            );
    }

    private static (IConsumer<T>, TaskCompletionSource<T[]>) GenerateMockConsumer<T>()
    {
        var mockConsumer = Substitute.For<IConsumer<T>>();
        mockConsumer.PartitionWritePolicy.Returns(new StandardChannelPolicy(bufferSize: 10, TimeSpan.FromMilliseconds(1)));
        mockConsumer.ExecutionPolicy.Returns(new SingleShotPolicy<T>(batchSize: 1, TimeSpan.FromMilliseconds(1)));

        var deliveryTcs = new TaskCompletionSource<T[]>();
        mockConsumer.ConsumeAsync(Arg.Any<ReadOnlyMemory<T>>(), Arg.Any<Action<T, Exception>>())
            .Returns(x =>
            {
                var messages = x.Arg<ReadOnlyMemory<T>>();
                deliveryTcs.TrySetResult(messages.ToArray());
                return Task.CompletedTask;
            });

        return (mockConsumer, deliveryTcs);
    }

    private static async Task<(bool success, Envelope<T> item, Exception exception)>
        PollDeadLetterQueue<T>(IDeadLetterHandler<T> deadLetters, string expectedId)
    {
        var success = false;
        Envelope<T> item = null!;
        Exception exception = null!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            (success, item, exception) = await deadLetters.TryGetAsync(expectedId);
            if (success) break;
        
            await Task.Delay(50, cts.Token);
        }

        return (success, item, exception);
    }
}