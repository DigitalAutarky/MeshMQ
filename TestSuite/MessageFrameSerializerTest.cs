using HackyMessage.Extension;
using HackyMessage.Metric;
using HackyMessage.Pooled.Pool;
using HackyMessage.Serialization;
using HackyMessage.Serialization.Serializers;
using MessagePack;
using Serilog;
using Serilog.Core;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class MessageFrameSerializerTest
{
    private readonly TestMessage _data = new() { Message = "Test" };
    private readonly string _serialized = "0000000691A454657374";
    
    [Test]
    [Category("Unit")]
    [Description("Serializes a message into the provided buffer and verifies it against the known good value")]
    public async Task SerializeSync_ShouldSerializeCorrectly_WhenGivenValidData()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var bufferTransactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        var serializer = new MessageFrameSerializer<TestMessage>();
        
        //serialize test message
        serializer.SerializeSync(bufferTransactionLease.Buffer, _data, 999);
        bufferTransactionLease.Buffer.Commit();
        
        //verify against known serialized data
        var serialized = Convert.ToHexString(bufferLease.Buffer.WrittenMemory.ToArray());
        Assert.That(serialized, Is.EqualTo(_serialized));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Serializes a message larger than max length which should throw an exception")]
    public async Task SerializeSync_ShouldThrow_WhenGivenDataLargerThanMaxLength()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var bufferTransactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        var serializer = new MessageFrameSerializer<TestMessage>();
        
        //verify failure
        Assert.Throws<ArgumentOutOfRangeException>(
            () => serializer.SerializeSync(bufferTransactionLease.Buffer, _data, 1));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Deserializes a known message from an input stream")]
    public async Task DeserializeAsync_ShouldSucceed_WhenGivenValidData()
    {
        //setup
        var stream = await CreatePreloadedStream(_serialized);
        
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var bufferTransactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        await using var streamTransactionLease = new StreamTransactionPool().Rent(stream);
        var serializer = new MessageFrameSerializer<TestMessage>();
        
        //deserialize message from stream
        var readLimit = stream.Length;
        var item = await serializer.DeserializeAsync(
            streamTransactionLease.Stream, bufferTransactionLease.Buffer, readLimit, 999);
        
        //verify message against known data
        Assert.That(item.Message, Is.EqualTo(_data.Message));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to deserialize a message larger than the given max length which should throw an exception")]
    public async Task DeserializeAsync_ShouldThrow_WhenGivenDataLargerThanMaxLength()
    {
        //setup
        var stream = await CreatePreloadedStream(_serialized);
        
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var bufferTransactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        await using var streamTransactionLease = new StreamTransactionPool().Rent(stream);
        var serializer = new MessageFrameSerializer<TestMessage>();

        var readLimit = stream.Length;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await serializer.DeserializeAsync(
            streamTransactionLease.Stream, bufferTransactionLease.Buffer, readLimit, 1));
    }

    private async Task<Stream> CreatePreloadedStream(string hexData)
    {
        var stream = new MemoryStream();
        await stream.WriteAsync(Convert.FromHexString(_serialized));
        await stream.FlushAsync();
        stream.Position = 0;
        return stream;
    }

    [MessagePackObject]
    internal record TestMessage
    {
        [Key(0)] public required string Message { get; init; }
    }
}