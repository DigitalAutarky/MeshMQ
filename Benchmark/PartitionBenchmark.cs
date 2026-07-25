using Benchmark.Common;
using BenchmarkDotNet.Attributes;
using HackyMessage.Core;
using HackyMessage.Core.Partition;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.Execution;
using HackyMessage.Core.Queue.DeadLetterQueue;
using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Factory;
using vtortola.WebSockets;

namespace Benchmark;

[MemoryDiagnoser]
public class PartitionBenchmark
{
    private const int PersistenceCapacity = 10 * 1024 * 1024;
    private const int HighWatermark = 10 * 1024 * 1024;
    private const int LowWatermark = 8 * 1024 * 1024;
    private const int WriteCount = 10000;
    
    private TemporaryFile? _dataFile;
    private TemporaryFile? _indexFile;

    private IPersistenceProvider<Envelope<string>>? _persistence;
    private Partition<string>? _partition;
    private NoOpStringConsumer? _consumer;
    private Envelope<string>? _item;
    
    
    [Params(100, 1000, 10000)]
    public int WriteBufferSize { get; set; }
    
    [Params(100, 1000, 10000)]
    public int ReadBatchSize { get; set; }
    
    
    [IterationSetup]
    public async ValueTask IterationSetup()
    {
        _dataFile = new TemporaryFile();
        _indexFile = new TemporaryFile();
        _persistence = await PersistenceProviderFactory.CreateFileBasedPersistenceAsync<Envelope<string>>(
            "topic", "queue", 1, HighWatermark, LowWatermark, PersistenceCapacity, _dataFile.FileName, _indexFile.FileName);

        var writePolicy = new StandardChannelPolicy(maxDelayInterval: Timeout.InfiniteTimeSpan, bufferSize: WriteBufferSize);
        var executionPolicy = new SingleShotPolicy<string>(ReadBatchSize, Timeout.InfiniteTimeSpan);
        _consumer = new NoOpStringConsumer(writePolicy,  executionPolicy, WriteCount);

        var deadletterHandler = new DeadLetterHandler<string>(WriteCount);
        
        _partition = new Partition<string>(_persistence, _consumer, deadletterHandler);
        _item = GenerateTestMessage<string>("id", "msg");
    }
    
    [IterationCleanup]
    public async ValueTask Cleanup()
    {
        await _partition!.DisposeAsync();
        await _indexFile!.DisposeAsync();
        await _dataFile!.DisposeAsync();
    }
    
    [Benchmark]
    public async Task EndToEndBenchmark()
    {
        // do not await AcceptAsync here or you will cause a deadlock
        for (var i = 0; i < WriteCount; i++) _partition!.AcceptAsync(_item);
        await _consumer!.SignalDone.WaitAsync(CancellationToken.None);
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
}