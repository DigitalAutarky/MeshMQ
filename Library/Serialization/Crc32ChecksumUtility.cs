using System.IO.Hashing;

namespace HackyMessage.Serialization;

public static class Crc32ChecksumUtility
{
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Crc32.HashToUInt32(data);
    }

    public static bool Verify(ReadOnlySpan<byte> data, uint checksum)
        => Compute(data) == checksum;
}