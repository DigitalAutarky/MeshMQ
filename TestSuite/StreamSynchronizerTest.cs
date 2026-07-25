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
public class StreamSynchronizerTest
{
    private readonly byte[] _magicHeader = [0xD0, 0x0D, 0xFE, 0xED];
    private readonly byte[] _dataArray1 = [0x01, 0x23, 0x45, 0x67];
    private readonly byte[] _dataArray2 = [0x67, 0x45, 0x23, 0x01];
    private readonly byte[] _dataArraySmallerThanMagicHeader =  [0x11, 0x22, 0x33];
    
    [Test]
    [Category("Unit")]
    [Description("Syncs to next magic header while landing right on top of it. Should read all data.")]
    public async Task SyncToNextHeaderAsync_ShouldReadData_WhenPerfectlyAlignedWithMagicHeaders()
    {
        //setup stream with data
        var stream = new MemoryStream();
        await WriteByteArray(stream, _magicHeader);
        await WriteByteArray(stream, _dataArray1);
        await WriteByteArray(stream, _magicHeader);
        await WriteByteArray(stream, _dataArray2);
        stream.Position = 0;

        //read and assert
        var foundMarker1 = await AssertNextReadEquals(stream, _magicHeader, _dataArray1);
        Assert.That(foundMarker1, Is.True);
        var foundMarker2 = await AssertNextReadEquals(stream, _magicHeader, _dataArray2);
        Assert.That(foundMarker2, Is.True);
        Assert.That(stream.Position, Is.EqualTo(16));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Syncs to next magic header while landing right on top of it. Should read all data.")]
    public async Task SyncToNextHeaderAsync_ShouldReadSecondDataOnly_WhenStartingAfterFirstMarker()
    {
        //setup stream with data
        var stream = new MemoryStream();
        await WriteByteArray(stream, _magicHeader);
        await WriteByteArray(stream, _dataArray1);
        await WriteByteArray(stream, _magicHeader);
        await WriteByteArray(stream, _dataArray2);
        
        //read and assert
        for (int i = 1; i < 8; i++)
        {
            stream.Position = i;
            var foundMarker = await AssertNextReadEquals(stream, _magicHeader, _dataArray2);
            Assert.That(foundMarker, Is.True);
            Assert.That(stream.Position, Is.EqualTo(16));
        }
    }
    
    [Test]
    [Category("Unit")]
    [Description("Doesnt contain magic sync marker which means syncing will advance the stream to (almost) its end.")]
    public async Task SyncToNextHeaderAsync_ShouldSkipAllData_WhenNoSyncMarkerPresent()
    {
        //setup stream with data
        var stream = new MemoryStream();
        await WriteByteArray(stream, _dataArray1);
        await WriteByteArray(stream, _dataArray2);
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, null);
        Assert.That(foundMarker, Is.False);
        
        //syncing backtracks slightly to handle case where the marker is split accross 2 scan buffers
        Assert.That(stream.Position, Is.EqualTo(5));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Doesnt contain magic sync marker which means syncing will advance the stream to (almost) its end.")]
    public async Task SyncToNextHeaderAsync_ShouldSkipAllDataOrNothing_WhenDataLessThanMagicMarker()
    {
        //setup stream with data
        var stream = new MemoryStream();
        await WriteByteArray(stream, _dataArraySmallerThanMagicHeader);
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, null);
        Assert.That(foundMarker, Is.False);
        
        //syncing backtracks slightly to handle case where the marker is split accross 2 scan buffers
        Assert.That(stream.Position, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Doesnt contain magic sync marker which means syncing will advance the stream to (almost) its end.")]
    public async Task SyncToNextHeaderAsync_ShouldSkipMostOrAllData_WhenDataLessThanMagicMarkerInMultipleScanFrames()
    {
        //setup stream with data
        var stream = new MemoryStream();
        for (int i = 0; i < 4096 / 4; i++)
        {
            await WriteByteArray(stream, _dataArray1);
        }
        
        await WriteByteArray(stream, _dataArraySmallerThanMagicHeader);
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, null);
        Assert.That(foundMarker, Is.False);
        
        //syncing backtracks slightly to handle case where the marker is split accross 2 scan buffers
        Assert.That(stream.Position, Is.EqualTo(4096));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies that synchronisation is successfull when requiring multiple scan passes")]
    public async Task SyncToNextHeaderAsync_ShouldSynchronizeProperly_WhenDataSpansMultipleScanPasses()
    {
        //setup stream with data
        var stream = new MemoryStream();
        for (int i = 0; i < 4096 / 4; i++) //synchronization reads 4096 bytes at a time
        {
            await WriteByteArray(stream, _dataArray1);
        }
        
        await WriteByteArray(stream, _magicHeader);
        await WriteByteArray(stream, _dataArray2);
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, _dataArray2);
        Assert.That(foundMarker, Is.True);
        Assert.That(stream.Position, Is.EqualTo(4104));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Verifies that when the synchronization marker spans two scan ranges it is properly detected.")]
    public async Task SyncToNextHeaderAsync_ShouldSynchronizeProperly_WhenMarkerSpansMultipleSearchRanges()
    {
        //setup stream with data
        var stream = new MemoryStream();
        for (int i = 0; i < (4096 / 4) - 1; i++) //synchronization reads 4096 bytes at a time
        {
            await WriteByteArray(stream, _dataArray1);
        }
        
        await WriteByteArray(stream, [0xAB]);  //three bytes left in first scan range
        await WriteByteArray(stream, _magicHeader); //three bytes in first an one byte in second scan range
        await WriteByteArray(stream, _dataArray2);  //expected data read
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, _dataArray2);
        Assert.That(foundMarker, Is.True);
        Assert.That(stream.Position, Is.EqualTo(4101));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to sync on an empty stream which should result in no changes to the stream.")]
    public async Task SyncToNextHeaderAsync_ShouldDoNothing_WhenStreamIsEmpty()
    {
        //setup stream with data
        var stream = new MemoryStream();
        stream.Position = 0;

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, null);
        Assert.That(foundMarker, Is.False);
        Assert.That(stream.Position, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Tries to sync when positioned at the end of a stream which should do nothing.")]
    public async Task SyncToNextHeaderAsync_ShouldDoNothing_WhenPositionedAtEndOfStream()
    {
        //setup stream with data
        var stream = new MemoryStream();
        await WriteByteArray(stream, _dataArray1);
        await WriteByteArray(stream, _dataArray2);
        stream.Position = 8; // 2 * 4 bytes written

        //read and assert
        var foundMarker = await AssertNextReadEquals(stream, _magicHeader, null);
        Assert.That(foundMarker, Is.False);
        Assert.That(stream.Position, Is.EqualTo(8));
    }

    private static async Task WriteByteArray(Stream stream, byte[] data)
    {
        await stream.WriteAsync(data, 0, data.Length);
    }

    private static async Task<bool> AssertNextReadEquals(Stream stream, byte[] magicHeader, byte[]? data)
    {
        var readLimit = stream.Length;
        var result = await StreamSynchronizer.SyncToNextHeaderAsync(stream, magicHeader, readLimit, CancellationToken.None);
        if (data != null)
        {
            var buffer = new byte[data.Length];
            await stream.ReadExactlyAsync(buffer);
            Assert.That(buffer.SequenceEqual(data.AsSpan()), Is.True);
        }

        return result;
    }
}