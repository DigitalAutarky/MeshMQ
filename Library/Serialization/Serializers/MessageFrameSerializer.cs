using System.Buffers.Binary;
using HackyMessage.Extension;
using HackyMessage.Pooled;
using HackyMessage.Pooled.Pool;
using MessagePack;
using Serilog;
using Serilog.Events;

namespace HackyMessage.Serialization.Serializers;

public sealed class MessageFrameSerializer<T>: IDisposable
{
    private readonly ArrayBufferWriterPool<byte> _bufferPool = new(1, 256);

    public void SerializeSync(BufferTransaction<byte> transaction, T message, long maxLength)
    {
        //serialize payload and ensure valid max length not exceeded
        using var buffer = _bufferPool.Rent();
        MessagePackSerializer.Serialize(buffer.Buffer, message, null, CancellationToken.None);
        var payloadSpan = buffer.Buffer.WrittenSpan;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, payloadSpan.Length, "payload max length");

        //serialize length
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, payloadSpan.Length);
        
        //write to buffer in correct order
        var lengthMemory = transaction.GetMemory(sizeof(int));
        lengthBytes.CopyTo(lengthMemory.Span);
        transaction.Advance(sizeof(int));

        var payloadSpanMemory = transaction.GetMemory(payloadSpan.Length);
        payloadSpan.CopyTo(payloadSpanMemory.Span);
        transaction.Advance(payloadSpan.Length);
    }

    public async Task<T> DeserializeAsync(StreamTransaction stream, BufferTransaction<byte> buffer, long readLimit, long maxLength)
    {
        //deserialze length
        var lengthMemory = buffer.GetExactReadBufferMemory(sizeof(int));
        await stream.ReadExactlyAsync(lengthMemory, readLimit, CancellationToken.None);
        buffer.Advance(sizeof(int));

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthMemory.Span);
        if (length < 0 || length > maxLength)
            throw new ArgumentOutOfRangeException(nameof(length), "Invalid payload length read from stream.");
        
        //deserialize message payload
        var messageMemory = buffer.GetExactReadBufferMemory(length);
        await stream.ReadExactlyAsync(messageMemory, readLimit, CancellationToken.None);
        buffer.Advance(length);
        
        //done
        return MessagePackSerializer.Deserialize<T>(messageMemory);
    }

    public void Dispose() => _bufferPool.Dispose();
}