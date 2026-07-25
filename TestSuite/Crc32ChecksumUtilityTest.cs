using HackyMessage.Metric;
using HackyMessage.Serialization;
using HackyMessage.Serialization.Serializers;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class Crc32ChecksumUtilityTest
{
    private readonly byte[] _data = [0xD0, 0x0D, 0xFE, 0xED];
    private readonly uint _checksum = 1531750176;
    
    [Test]
    [Category("Unit")]
    [Description("Calculates the checksum for the given byte array.")]
    public void Compute_ShouldComputeCorrectChecksum_WhenGivenByteArray()
    {
        //compute checksum for well known data
        var checksum = Crc32ChecksumUtility.Compute(_data);
        
        //compare with well known hex checksum
        Assert.That(checksum, Is.EqualTo(_checksum));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies the checksum for the given byte array.")]
    public void Verify_ShouldSucceed_WhenGivenByteArrayAndChecksum()
    {
        //compute checksum for well known data
        var isCorrect = Crc32ChecksumUtility.Verify(_data, _checksum);
        
        //compare with well known hex checksum
        Assert.That(isCorrect, Is.True);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies a wrong checksum for the given byte array. Therefore it should fail verification.")]
    public void Verify_ShouldFail_WhenGivenWrongChecksum()
    {
        //verify with a checksum mismatch
        var isCorrect1 = Crc32ChecksumUtility.Verify(_data, _checksum+1);
        var isCorrect2 = Crc32ChecksumUtility.Verify(_data, _checksum-1);
        
        //compare with well known hex checksum
        Assert.That(isCorrect1, Is.False);
        Assert.That(isCorrect2, Is.False);
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies a wrong data array for the given checksum. Therefore it should fail verification.")]
    public void Verify_ShouldFail_WhenGivenWrongData()
    {
        //verify with a checksum mismatch
        var (data1, data2) = GenerateCorruptedData(_data);
        var isCorrect1 = Crc32ChecksumUtility.Verify(data1, _checksum);
        var isCorrect2 = Crc32ChecksumUtility.Verify(data2, _checksum);
        
        //compare with well known hex checksum
        Assert.That(isCorrect1, Is.False);
        Assert.That(isCorrect2, Is.False);
    }

    private (byte[] minusOne, byte[] plusOne) GenerateCorruptedData(byte[] data)
    {
        var minusOne = new byte[data.Length];
        data.CopyTo(minusOne, 0);
        minusOne[^1] -= 1;
        
        var plusOne = new byte[data.Length];
        data.CopyTo(plusOne, 0);
        plusOne[^1] += 1;
        
        return (minusOne, plusOne);
    }
}