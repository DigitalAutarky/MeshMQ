using System.Collections;
using DotNext.Collections.Generic;
using DotNext.Threading;
using HackyMessage;
using HackyMessage.Common;
using HackyMessage.Core;
using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Disk;
using HackyMessage.Persistence.Provider.Disk.Index;
using HackyMessage.Persistence.Provider.Factory;
using HackyMessage.Pooled;
using HackyMessage.Pooled.Pool;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TestSuite.Common;
using Index = HackyMessage.Persistence.Provider.Disk.Index.Index;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class WriteAheadLogTest
{
    //If you need to update this sample of a valid serialized message the easiest way to get
    //an up to date version is to run the roundripping test and look for the full entry value.
    private const string ValidHackyMessageOfHackyTestMessage =
        "D00DFEED0000003A94A36F6E65A6436F72313233D7FF278A48B069CFD10C9AA454657374010203CA40900000CB3FF3AE147AE147AEC4020102930102039301020380E98F7739";

    
    [Test]
    [Category("Integration")]
    [Description("Performs a round-trip saving the test message to a file backed storage and then reading it back")]
    public async Task StoreAndFetch_ShouldSucceed_WhenRoundtrippingMessage()
    {
        //setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a test message
        await StoreSingleTestMessage(wal, "one");
        
        //fetch the stored message
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchSingleTestMessage(wal, resultList);
        
        //assertions, compare result with values of test message
        var item = resultList.ElementAt(0);
        
        Assert.That(item, Is.Not.Null);
        Assert.That(item.Id, Is.EqualTo("one"));
        Assert.That(item.CorrelationId, Is.EqualTo("Cor123"));
        Assert.That(item.Message, Is.Not.Null);
        Assert.That(item.Message.String, Is.EqualTo("Test"));
        Assert.That(item.Message.Int, Is.EqualTo(1));
        Assert.That(item.Message.Short, Is.EqualTo(2));
        Assert.That(item.Message.Long, Is.EqualTo(3));
        Assert.That(item.Message.Float, Is.EqualTo(4.5f));
        Assert.That(item.Message.Double, Is.EqualTo(1.23d));
        Assert.That(item.Message.Bytes, Is.EqualTo([0x01, 0x02]));
        Assert.That(item.Message.List, Has.Count.EqualTo(3));
        Assert.That(item.Message.Set, Has.Count.EqualTo(3));
        Assert.That(item.Message.Dictionary, Is.Not.Null);
    }
    
    
    [Test]
    [Category("Integration")]
    [Description("Writes two messages then proceeds to read one and reinitialize before reading the second one")]
    public async Task StoreAndFetch_ShouldRememberProcessedOffset_WhenReadingAcrossMultipleInstances()
    {
        //setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store two test messages
        await StoreSingleTestMessage(wal1, "one");
        await StoreSingleTestMessage(wal1, "two");
        
        //fetch the first message and confirm it as processed
        var resultList1 = new LinkedList<Envelope<TestMessage>>();
        var processed = await FetchSingleTestMessage(wal1, resultList1);
        await wal1.ConfirmProcessed(processed);
        
        Assert.That(resultList1.Count, Is.EqualTo(1));
        Assert.That(resultList1, Has.One.Matches<Envelope<TestMessage>>
            (m => m.Id.Equals("one")));
        
        //create a new instance of the wal which should sync through the index
        await using var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //fetch the second message
        var resultList2 = new LinkedList<Envelope<TestMessage>>();
        var _ = await FetchSingleTestMessage(wal2, resultList2);
        
        Assert.That(resultList2.Count, Is.EqualTo(1));
        Assert.That(resultList2, Has.One.Matches<Envelope<TestMessage>>
            (m => m.Id.Equals("two")));
    }
    
    
    [Test]
    [Category("Integration")]
    [Description("Writes a known valid message to the data file and then retrieves and deserializes it")]
    public async Task StoreAndFetch_ShouldSucceed_WhenEncounteringValidMessage()
    {
        //setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        await FileAppender.AppendAsync(dataFile.FileName, ValidHackyMessageOfHackyTestMessage);
        var index = new Index(indexFile.FileName);
        var fileInfo = new FileInfo(dataFile.FileName);
        await index.AdvanceAsync(IndexKey.WritePosition, fileInfo.Length, CancellationToken.None);
        await index.DisposeAsync();
        
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //retrieve the written test message
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchSingleTestMessage(wal, resultList);
        
        //assertions
        Assert.That(resultList.Count, Is.EqualTo(1));
        Assert.That(resultList.ElementAt(0).Message, Is.Not.Null);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Adds torn frame data in between two store operations then tries to raed and deserialize all messages")]
    public async Task StoreAndFetch_ShouldSkip_WhenEncounteringTornFrame()
    {
        var validMessageBytes = Convert.FromHexString(ValidHackyMessageOfHackyTestMessage).AsMemory();
        for(var i = 1; i < validMessageBytes.Length-1; i++)
        {
            //setup
            await using var dataFile = new TemporaryFile();
            await using var indexFile = new TemporaryFile();
            await using var wal = await PersistenceProviderFactory
                .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                    "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
            
            //write data
            await StoreSingleTestMessage(wal, "one");
            await FileAppender.AppendAsync(dataFile.FileName, validMessageBytes.Slice(0, i).ToArray());
            await StoreSingleTestMessage(wal, "two");
            
            //collect results from multiple fetch invocations
            var resultList = new LinkedList<Envelope<TestMessage>>();
            await FetchSingleTestMessage(wal, resultList);
            await FetchSingleTestMessage(wal, resultList);
            await FetchSingleTestMessage(wal, resultList);
            
            //assertions
            Assert.That(resultList, Has.Count.EqualTo(2));
            
            Assert.That(resultList, Has.One.Matches<Envelope<TestMessage>>
                (m => m.Id.Equals("one")));
            
            Assert.That(resultList, Has.One.Matches<Envelope<TestMessage>>
                (m => m.Id.Equals("two")));
        }
    }
    
    
    [Test]
    [Category("Integration")]
    [Description("Adds corrupted frame data in between two store operations then tries to raed and deserialize all messages")]
    public async Task StoreAndFetch_ShouldSkip_WhenEncounteringCorruptedFrame()
    {
        var validMessageBytes = Convert.FromHexString(ValidHackyMessageOfHackyTestMessage);
        for(var i = 0; i < validMessageBytes.Length; i++)
        {
            //setup
            await using var dataFile = new TemporaryFile();
            await using var indexFile = new TemporaryFile();
            await using var wal = await PersistenceProviderFactory
                .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                    "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
            
            //setup corrupted nessage
            var corruptedMessageBytes = new byte[validMessageBytes.Length];
            validMessageBytes.CopyTo(corruptedMessageBytes);
            corruptedMessageBytes[i] += 1;
                
            //write data
            await StoreSingleTestMessage(wal, "one");
            await FileAppender.AppendAsync(dataFile.FileName, corruptedMessageBytes);
            await StoreSingleTestMessage(wal, "two");
            
            //collect results from multiple fetch invocations
            var resultList = new LinkedList<Envelope<TestMessage>>();
            await FetchSingleTestMessage(wal, resultList);
            await FetchSingleTestMessage(wal, resultList);
            await FetchSingleTestMessage(wal, resultList);
            
            //assertions
            Assert.That(resultList, Has.Count.EqualTo(2));
            
            Assert.That(resultList, Has.One.Matches<Envelope<TestMessage>>
                (m => m.Id.Equals("one")));
            
            Assert.That(resultList, Has.One.Matches<Envelope<TestMessage>>
                (m => m.Id.Equals("two")));
        }
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores a single test message then tries to fetch two test messages which will result in a timeout")]
    public async Task StoreAndFetch_ShouldReturnPartialResult_WhenFetchTimeoutIsReached()
    {
        //setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a single test message
        await StoreSingleTestMessage(wal, "one");
        
        //fetch the stored message
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchNTestMessages(wal, resultList, 2);
        
        //assertions
        Assert.That(resultList, Has.Count.EqualTo(1));
        Assert.That(resultList.ElementAt(0).Id, Is.EqualTo("one"));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores a test message the start a fetch operation followed by another store while still within timeout limits")]
    public async Task StoreAndFetch_ShouldReturnFullResult_WhileWaitingForTimeout()
    {
        //setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a test message
        await StoreSingleTestMessage(wal, "one");
        
        //start a fetch operation
        var resultList = new LinkedList<Envelope<TestMessage>>();
        var fetchTask = FetchNTestMessages(wal, resultList, 2);
        
        //store another test message
        await StoreSingleTestMessage(wal, "two");
        
        //wait for fetch to complete
        await fetchTask;
        
        //assertions
        Assert.That(resultList, Has.Count.EqualTo(2));
        Assert.That(resultList.ElementAt(0).Id, Is.EqualTo("one"));
        Assert.That(resultList.ElementAt(1).Id, Is.EqualTo("two"));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores a test message then uses a new wal with a max size so the wal is full exactly. Then asserts further usage throws exceptions.")]
    public async Task StoreAndFetch_ShouldThrow_WhenFullAndFullyProcessedExactly()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure first wal instance
        await using var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a test message using first wal instance
        var (bytesWritten, _) = await StoreSingleTestMessage(wal1, "one");
        
        //configure second wal instance with maxLength exactly the size of the message we stored using the first wal
        await using var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, bytesWritten, dataFile.FileName, indexFile.FileName);
        
        //fetch the previously stored message so the read position should be at the end
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchSingleTestMessage(wal2, resultList);
        
        //now reads and writes to the second wal which should be full and read to the end should fail
        var (_, result) = await StoreSingleTestMessage(wal2, "two");
        Assert.That(result.Value, Is.TypeOf<PersistenceCapacityReached>());
        
        resultList.Clear();
        await FetchSingleTestMessage(wal2, resultList);
        Assert.That(resultList, Has.Count.EqualTo(0));
        Assert.That(wal2.IsFullyProcessed, Is.True);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores a test message then uses a new wal with a max size so the wal max size is 1 byte smaller than the message. Then asserts further usage throws exceptions.")]
    public async Task StoreAndFetch_ShouldComplete_WhenFullAndFullyProcessedExceedingMaxSize()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure first wal instance
        await using var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a test message using first wal instance
        var (bytesWritten, _) = await StoreSingleTestMessage(wal1, "one");
        
        //configure second wal instance with maxLength exactly the size of the message we stored using the first wal
        await using var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, bytesWritten, dataFile.FileName, indexFile.FileName);
        
        //fetch the previously stored message so the read position should be at the end
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchSingleTestMessage(wal2, resultList);
        
        //now reads and writes to the second wal which should be full and read to the end should fail
        var (_, result) = await StoreSingleTestMessage(wal2, "two");
        Assert.That(result.Value, Is.TypeOf<PersistenceCapacityReached>());
        
        resultList.Clear();
        await FetchSingleTestMessage(wal2, resultList);
        Assert.That(resultList, Has.Count.EqualTo(0));
        Assert.That(wal2.IsFullyProcessed, Is.True);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores a test message then uses a new wal with a max size so the wal max size is 1 byte bigger than the message. Then asserts further usage allows 1 more write then throws exceptions.")]
    public async Task StoreAndFetch_ShouldAllowOneMoreWrite_WhenAlmostFullWithOneByteAvailableLeft()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure first wal instance
        await using var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 4092, dataFile.FileName, indexFile.FileName);
        
        //store a test message using first wal instance
        var (bytesWritten, _) = await StoreSingleTestMessage(wal1, "one");
        
        //configure second wal instance with maxLength exactly the size of the message we stored using the first wal
        await using var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, bytesWritten+1, dataFile.FileName, indexFile.FileName);
        
        //Should allow one more write since we have one byte remaining capacity
        var (_, completionTasks, _) = await StoreNTestMessages(wal2, "two", "three");
        
        //verify the two messages have been sucessfully stored
        foreach(var task in completionTasks)
        {
            var r = await task;
            Assert.That(r.Value, Is.TypeOf<Success>());
        }
        
        //read the two test messages we stored
        await FetchNTestMessages(wal2, new LinkedList<Envelope<TestMessage>>(), 3);
        
        //now reads and writes to the second wal which should be full and read to the end should fail
        var (_, result) = await StoreSingleTestMessage(wal2, "four");
        Assert.That(result.Value, Is.TypeOf<PersistenceCapacityReached>());
        
        var resultList = new LinkedList<Envelope<TestMessage>>();
        await FetchSingleTestMessage(wal2, resultList);
        Assert.That(resultList, Has.Count.EqualTo(0));
        Assert.That(wal2.IsFullyProcessed, Is.True);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Fills the log to capacity then tries to write some more items which should result in them being faulted with an exception.")]
    public async Task Store_ShouldFaultAllItems_WhenFull()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure wal instance
        await using var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 64, dataFile.FileName, indexFile.FileName);
        
        //store a test message
        var (bytesWritten, _) = await StoreSingleTestMessage(wal1, "one");
        
        //setup new wal at capacity
        await using var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, bytesWritten, dataFile.FileName, indexFile.FileName);
        
        //All of these should be faulted
        var (_, completionTasks, _) = await StoreNTestMessages(wal2, "two", "three", "four", "five");
        
        //verify tasks return full status
        foreach(var task in completionTasks)
        {
            var result = await task;
            Assert.That(result.Value, Is.TypeOf<PersistenceCapacityReached>());
        }
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to store a bunch of messages and encounters a cancellation while trying to acquire the io lock. All messages should be faulted.")]
    public async Task Store_ShouldCancelAllMessages_WhenAcquiringTheIoLockIsCancelled()
    {
        //global setup
        var mockIoLock = Substitute.For<IMyAsyncLock>();
        
        mockIoLock.When(x => x.AcquireAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()))
            .Do(x => throw new OperationCanceledException());
        
        var ioContext = CreateMockIoContext(4096, 2048, 4096) with { WriteLock = mockIoLock };
        await using var wal = new WriteAheadLog<Envelope<TestMessage>>(ioContext, () => { });

        //try to store a bunch of messages
        var (_, completionTasks, _) = await StoreNTestMessages(wal, "one", "two", "three", "four", "five");
        
        //wait for the store to complete, suppress exceptions since we will verify task status afterwards
        foreach(var task in completionTasks)
        {
            var result = await task;
            Assert.That(result.Value, Is.TypeOf<Cancelled>());
        }
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to store a bunch of messages and encounters an exception while writing to disk. All messages should be faulted.")]
    public async Task Store_ShouldFaultAllMessagesWith_WhenWritingToDiskFails()
    {
        //global setup
        var mockWriter = Substitute.For<Stream>();
        
        mockWriter.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Throws(new Exception());
        
        var ioContext = CreateMockIoContext(4096, 2048, 4096) with { LogWriter = mockWriter };
        await using var wal = new WriteAheadLog<Envelope<TestMessage>>(ioContext, () => { });

        //try to store a bunch of messages
        var (_, completionTasks, _) = await StoreNTestMessages(wal, "one", "two", "three", "four", "five");
        
        //wait for the store to complete, suppress exceptions since we will verify task status afterwards
        foreach(var task in completionTasks)
        {
            var result = await task;
            Assert.That(result.Value, Is.TypeOf<PersistenceFailure>());
        }
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to store a bunch of messages and encounters an exception while flushing to disk. All messages should be faulted.")]
    public async Task Store_ShouldFaultAllMessagesWith_WhenFlushingToDiskFails()
    {
        //global setup
        var mockWriter = Substitute.For<Stream>();
        
        mockWriter.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        
        mockWriter.FlushAsync(Arg.Any<CancellationToken>())
            .Throws(new Exception());
        
        var ioContext = CreateMockIoContext(4096, 2048,4096) with { LogWriter = mockWriter };
        await using var wal = new WriteAheadLog<Envelope<TestMessage>>(ioContext, () => { });

        //try to store a bunch of messages
        var (_, completionTasks, _) = await StoreNTestMessages(wal, "one", "two", "three", "four", "five");
        
        //verify store failes
        foreach(var task in completionTasks)
        {
            var result = await task;
            Assert.That(result.Value, Is.TypeOf<PersistenceFailure>());
        }
    }
    
    [Test]
    [Category("Integration")]
    [Description("Tries to store a bunch of messages and encounters an exception while flushing to disk. All messages should be faulted.")]
    public async Task Store_ShouldReturnZeroBytesWritten_WhenWorkItemsAreEmpty()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure wal instance
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 64, dataFile.FileName, indexFile.FileName);

        //invoke store and verify result
        var result = await wal.EnqueueAsync(ReadOnlyMemory<WorkItem<Envelope<TestMessage>, PersistenceResult>>.Empty);
        Assert.That(result, Is.Zero);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Disposes a wal which has been fully processed and verifies that its files are deleted on disposal.")]
    public async Task StoreAndFetch_ShouldDeleteFiles_WhenDisposedAfterFullyProcessed()
    {
        //global setup
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        
        //configure wal instance
        var wal1 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, 64, dataFile.FileName, indexFile.FileName);
        
        //store a test message
        var (bytesWritten, _) = await StoreSingleTestMessage(wal1, "one");
        
        //dispose wal and verify files still exist
        wal1.Dispose();
        Assert.That(File.Exists(dataFile.FileName), Is.True);
        Assert.That(File.Exists(indexFile.FileName), Is.True);
        
        //setup new wal which is already full
        var wal2 = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<Envelope<TestMessage>>(
                "topic", "queue", 1, 4096, 2048, bytesWritten, dataFile.FileName, indexFile.FileName);
        
        //fetch the stored message from the new wal which should render it fully processed
        await FetchSingleTestMessage(wal2, new LinkedList<Envelope<TestMessage>>());
        
        //dispose wal and verify files no longer exist
        wal2.Dispose();
        Assert.That(File.Exists(dataFile.FileName), Is.False);
        Assert.That(File.Exists(indexFile.FileName), Is.False);
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes enough messages to trigger the backpressure and verifies that it resolves when the reader catches up.")]
    public async Task StoreAndFetch_ShouldBlock_WhenHighWatermarkExceededAndInBlockingMode()
    {
        var validMessageBytes = new byte[] {0x01, 0x02, 0x03, 0x04};

        // SETUP: Increased low watermark to 24 to account for MessagePack serialization headers.
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<byte[]>(
                "topic", "queue", 1, 32, 24, 4092, dataFile.FileName, indexFile.FileName);
        
        // ACT - Cross the high watermark
        await StoreSingleTestMessage(wal, validMessageBytes);
        await StoreSingleTestMessage(wal, validMessageBytes);
    
        // Initiate the 3rd write
        var blockedTask = StoreSingleTestMessage(wal, validMessageBytes).AsTask();
    
        // ASSERT 1 - Verify backpressure is applied
        // If the task correctly hits the AsyncManualResetEvent, it won't complete.
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await blockedTask.WaitAsync(TimeSpan.FromMilliseconds(200)),
            "The task should be blocked because the high watermark was exceeded."
        );
    
        // ACT - Notify processed so the backpressure is relieved
        await wal.NotifyExecutionComplete(32);

        // ASSERT 2 - Verify backpressure resolves
        // WaitAsync ensures we don't hang the test suite indefinitely if this fails.
        await Assert.DoesNotThrowAsync(
            async () => await blockedTask.WaitAsync(TimeSpan.FromSeconds(2)),
            "The blocked task should complete after the backlog drops below the low watermark."
        );
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes enough messages to trigger the backpressure and verifies that additional stores fail.")]
    public async Task StoreAndFetch_ShoulFail_WhenHighWatermarkExceededAndInNonBlockingMode()
    {
        var validMessageBytes = new byte[] {0x01, 0x02, 0x03, 0x04};

        // SETUP: Increased low watermark to 24 to account for MessagePack serialization headers.
        await using var dataFile = new TemporaryFile();
        await using var indexFile = new TemporaryFile();
        await using var wal = await PersistenceProviderFactory
            .CreateFileBasedPersistenceAsync<byte[]>(
                "topic", "queue", 1, 32, 24, 4092, dataFile.FileName, indexFile.FileName);
        
        // ACT - Cross the high watermark
        await StoreSingleTestMessage(wal, validMessageBytes, false);
        await StoreSingleTestMessage(wal, validMessageBytes, false);
    
        // Initiate the 3rd write & assert that it fails
        var blocked = await StoreSingleTestMessage(wal, validMessageBytes, false).AsTask();
        Assert.That(blocked.Value, Is.TypeOf<RetryLater>());
    
        // ACT Notify processed so the backpressure is relieved
        await wal.NotifyExecutionComplete(32);

        // Initiate the 4th write & assert that it succeeds
        var unblocked = await StoreSingleTestMessage(wal, validMessageBytes, false).AsTask();
        Assert.That(unblocked.Value, Is.TypeOf<Success>());
    }
    
    private static async Task<(int, PersistenceResult)> StoreSingleTestMessage(IPersistenceProvider<Envelope<TestMessage>> wal, string id)
    {
        using var csPool = new ValueTaskCompletionSourcePool<PersistenceResult>();
        await using var csLease = csPool.Rent();

        var item = CreateTestMessage(id);
        var cs = csLease.Source;

        using var itemPool = new WorkItemPool<Envelope<TestMessage>, PersistenceResult>();
        await using var  workItemLease = itemPool.Rent(item, cs);
        
        var workItems = new WorkItem<Envelope<TestMessage>, PersistenceResult>[1];
        workItems[0] = workItemLease.WorkItem;
        
        var bytesWritten = await wal.EnqueueAsync(workItems.AsMemory());
        var result = await workItemLease.WorkItem.CompletionSource!.ValueTask;
        return (bytesWritten, result);
    }
    
    private static async ValueTask<PersistenceResult> StoreSingleTestMessage<T>(IPersistenceProvider<T> wal, T item, bool isBlocking = true)
    {
        using var csPool = new ValueTaskCompletionSourcePool<PersistenceResult>();
        await using var csLease = csPool.Rent();
        
        var cs = csLease.Source;

        using var itemPool = new WorkItemPool<T, PersistenceResult>();
        await using var  workItemLease = itemPool.Rent(item, cs);
        
        var workItems = new WorkItem<T, PersistenceResult>[1];
        workItems[0] = workItemLease.WorkItem;
        
        await wal.EnqueueAsync(workItems.AsMemory(), isBlocking);
        return await workItemLease.WorkItem.CompletionSource!.ValueTask;
    }
    
    private static async Task<(int bytesWritten, List<ValueTask<PersistenceResult>> completionTasks, Exception ex)> StoreNTestMessages
        (IPersistenceProvider<Envelope<TestMessage>> wal, params string[] ids)
    {
        var batch = new WorkItem<Envelope<TestMessage>, PersistenceResult>[ids.Length];
        var completionTasks = new List<ValueTask<PersistenceResult>>();
        
        var csPool = new ValueTaskCompletionSourcePool<PersistenceResult>();
        var itemPool = new WorkItemPool<Envelope<TestMessage>, PersistenceResult>();

        for (var i = 0; i < ids.Length; i++)
        {
            var msg = CreateTestMessage(ids[i]);
            var cs = csPool.Rent().Source;
            batch[i] = itemPool.Rent(msg, cs).WorkItem;
            completionTasks.Add(cs.ValueTask);
        }

        var bytesWritten = -1;
        Exception? exception = null;
        try
        {
            bytesWritten = await wal.EnqueueAsync(batch);
        }
        catch (Exception e)
        {
            exception = e;
        }
        
        return (bytesWritten!, completionTasks!, exception!);
    }

    private static async Task<long> FetchSingleTestMessage(
        IPersistenceProvider<Envelope<TestMessage>> wal, LinkedList<Envelope<TestMessage>> collection)
    {
        var buffer = new Envelope<TestMessage>[1];
        var (count, processed) = await wal.DequeueAsync(buffer, TimeSpan.FromMilliseconds(100));
        if(count  > 0) collection.AddAll(buffer[..count]);
        return processed;
    }
    
    private static async Task<long> FetchSingleTestMessage<T>(IPersistenceProvider<T> wal, LinkedList<T> collection)
    {
        var buffer = new T[1];
        var (count, processed) = await wal.DequeueAsync(buffer, TimeSpan.FromMilliseconds(100));
        if(count  > 0) collection.AddAll(buffer[..count]);
        return processed;
    }
    
    private static async Task<long> FetchNTestMessages(
        IPersistenceProvider<Envelope<TestMessage>> wal, LinkedList<Envelope<TestMessage>> collection, int n)
    {
        var buffer = new Envelope<TestMessage>[n];
        var (count, processed) = await wal.DequeueAsync(buffer, TimeSpan.FromMilliseconds(100));
        if(count  > 0) collection.AddAll(buffer[..count]);
        return processed;
    }

    private static Envelope<TestMessage> CreateTestMessage(string id)
    {
        var testMessage = new TestMessage
        {
            String = "Test",
            Int = 1,
            Short = 2,
            Long = 3,
            Float = 4.5f,
            Double = 1.23d,
            Bytes = [0x01, 0x02],
            List = [1, 2, 3],
            Set = [1, 2, 3],
            Dictionary = new Dictionary<int, int>()
        };
        
        return new Envelope<TestMessage>()
        { 
            Id = id,
            CorrelationId = "Cor123",
            CreatedAt = DateTime.UtcNow,
            Message = testMessage
        };
    }

    private static IoContext CreateMockIoContext(long highWatermark, long lowWatermark, long maxSize)
    {
        var logWriter = Substitute.For<Stream>();
        var logReader = Substitute.For<Stream>();
        var writeLock = Substitute.For<MyAsyncLock>();
        var readLock = Substitute.For<MyAsyncLock>();
        var index = Substitute.For<IIndexProvider>();
        return new IoContext(logWriter, logReader, writeLock, readLock, index, highWatermark, lowWatermark, maxSize);
    }
}