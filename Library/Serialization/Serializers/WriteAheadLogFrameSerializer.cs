using System.Buffers.Binary;
using HackyMessage.Extension;
using HackyMessage.Pooled;
using Serilog;
using Serilog.Events;

namespace HackyMessage.Serialization.Serializers;

public sealed class WriteAheadLogFrameSerializer<T>: IDisposable
{
    private readonly byte[] _magicHeader = [0xD0, 0x0D, 0xFE, 0xED];
    private readonly MessageFrameSerializer<T> _messageFrame = new();

    public void SerializeSync(BufferTransaction<byte> bufferTransaction, T message, long maxLength)
    {
        //save index so we can slice only the bytes we have added for the result
        var start = bufferTransaction.GetLength();
        
        //write magic header
        var magicHeaderMemory = bufferTransaction.GetMemory(_magicHeader.Length);
        _magicHeader.CopyTo(magicHeaderMemory);
        bufferTransaction.Advance(_magicHeader.Length);
        
        //serialize the message
        _messageFrame.SerializeSync(bufferTransaction, message, maxLength);
        
        //append checksum
        var writtenBytes = bufferTransaction.GetMemory(start, bufferTransaction.GetLength() - start);
        var checksumMemory = bufferTransaction.GetMemory(sizeof(int));
        var checksum = Crc32ChecksumUtility.Compute(writtenBytes.Span);

        BinaryPrimitives.WriteUInt32BigEndian(checksumMemory.Span, checksum);
        bufferTransaction.Advance(sizeof(int));
        
        //log full entry
        var end = bufferTransaction.GetLength();
        var len = end - start;
    }

    public async Task<T> DeserializeAsync(
        StreamTransaction stream, BufferTransaction<byte> buffer, long maxLength, long readLimit)
    {
        //save index so we can slice only the bytes we have added for the result
        var start = buffer.GetLength();
        
        //skip past next marker
        if (!await stream.SyncToNextHeaderAsync(_magicHeader, readLimit, CancellationToken.None))
            throw new EndOfStreamException();
        
        //commit the current read index so we wont end up proccessing the
        //same corrupt data and over
        stream.Commit();
        
        //add magic marker which we just skipped past
        var magicHeaderMemory = buffer.GetExactReadBufferMemory(_magicHeader.Length);
        _magicHeader.CopyTo(magicHeaderMemory);
        buffer.Advance(_magicHeader.Length);

        //deserialize and add message frame
        var result = await _messageFrame.DeserializeAsync(stream, buffer, readLimit, maxLength);
        
        //verify and add checksum
        var checksumMemory = buffer.GetExactReadBufferMemory(sizeof(int));
        await stream.ReadExactlyAsync(checksumMemory, readLimit, CancellationToken.None);
 
        var checksum = BinaryPrimitives.ReadUInt32BigEndian(checksumMemory.Span);
        var readBytes = buffer.GetMemory(start, buffer.GetLength() - start);
        if(!Crc32ChecksumUtility.Verify(readBytes.Span, checksum))
            throw new InvalidDataException();

        buffer.Advance(sizeof(int));
        
        //done
        return result;
    }
    
    public void Dispose() => _messageFrame.Dispose();
}