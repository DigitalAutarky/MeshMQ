using System.Buffers;

namespace HackyMessage.Serialization;

public static class StreamSynchronizer
{
    
    public static async ValueTask<bool> SyncToNextHeaderAsync(
        Stream stream, ReadOnlyMemory<byte> magicHeader, long readLimit,  CancellationToken cancellationToken)
    {
        var startPos = stream.Position;
        var availableBytes = readLimit - startPos;
        
        // If we don't even have enough bytes for a header, abort early.
        if (availableBytes < magicHeader.Length)
            return false;

        var tempBuffer = ArrayPool<byte>.Shared.Rent(magicHeader.Length);
        try
        {
            // We know we have at least magicHeader.Length bytes available based on limitPosition
            var read = await stream.ReadAsync(tempBuffer.AsMemory(0, magicHeader.Length), cancellationToken);
            if (read < magicHeader.Length)
            {
                stream.Position = startPos;
                return false;
            }

            if (tempBuffer.AsSpan(0, magicHeader.Length).SequenceEqual(magicHeader.Span))
                 return true; 
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
        
        stream.Position = startPos;
        var scanBuffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            while (true)
            {
                var scanStartPos = stream.Position;
                var scanAvailable = readLimit - scanStartPos;
                
                if (scanAvailable < magicHeader.Length)
                {
                    stream.Position = scanStartPos; 
                    return false;
                }

                // Only request exactly what is allowed up to our limit boundary
                int toRead = (int)Math.Min(scanBuffer.Length, scanAvailable);
                var bytesRead = await stream.ReadAsync(scanBuffer.AsMemory(0, toRead), cancellationToken);
                
                if (bytesRead < magicHeader.Length)
                {
                    stream.Position = scanStartPos;
                    return false;
                }

                var spanToCheck = scanBuffer.AsSpan(0, bytesRead);
                var index = spanToCheck.IndexOf(magicHeader.Span);

                if (index >= 0)
                {
                    var newPos = scanStartPos + index;
                    stream.Position = newPos + magicHeader.Length;
                    return true;
                }

                var backTrack = magicHeader.Length - 1;
                stream.Position = scanStartPos + bytesRead - backTrack;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scanBuffer);
        }
    }
}