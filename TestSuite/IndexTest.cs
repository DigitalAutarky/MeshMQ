using HackyMessage.Persistence;
using HackyMessage.Persistence.Provider.Disk.Index;
using TestSuite.Common;
using Index = HackyMessage.Persistence.Provider.Disk.Index.Index;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class IndexTest
{
    [Test]
    [Category("Integration")]
    [Description("Performs a round-trip saving values to a file backed index and then reading them back")]
    public async Task StoreAndFetch_ShouldSucceed_WhenRoundtrippingIndexValue()
    {
        //setup
        await using var indexFile = new TemporaryFile();
        await using var index = new Index(indexFile.FileName);
        
        //store values to index
        await index.AdvanceAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.WritePosition, 456, CancellationToken.None);
        
        //fetch the stored values
        var readPosition = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
        var writePosition = await index.GetOrDefaultAsync(IndexKey.WritePosition, 0, CancellationToken.None);
        
        //assertions
        Assert.That(readPosition, Is.EqualTo(123));
        Assert.That(writePosition, Is.EqualTo(456));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Reads an index key that does not exist which should result in the default value being returned")]
    public async Task Fetch_ShouldReturnDefaultValue_WhenKeyDoesNotExist()
    {
        //setup
        await using var indexFile = new TemporaryFile();
        await using var index = new Index(indexFile.FileName);
        
        //fetch the non-existant key value
        var processedUbtil = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        
        //assertions
        Assert.That(processedUbtil, Is.EqualTo(123));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Updates the same key twice with increasing values which should result in the index returning the second value")]
    public async Task StoreAndFetch_ShouldSucceed_WhenStoringBiggerValue()
    {
        //setup
        await using var indexFile = new TemporaryFile();
        await using var index = new Index(indexFile.FileName);
        
        //store values to index
        await index.AdvanceAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.ReadPosition, 456, CancellationToken.None);
        
        //fetch the stored value
        var processedUbtil = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
        
        //assertions
        Assert.That(processedUbtil, Is.EqualTo(456));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Updates the same key twice with decreasing values which should result in the index returning the first value")]
    public async Task StoreAndFetch_ShouldSkip_WhenStoringSmallerValue()
    {
        //setup
        await using var indexFile = new TemporaryFile();
        await using var index = new Index(indexFile.FileName);
        
        //store values to index
        await index.AdvanceAsync(IndexKey.ReadPosition, 456, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        
        //fetch the stored value
        var processedUbtil = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
        
        //assertions
        Assert.That(processedUbtil, Is.EqualTo(456));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Stores some values in an index then creates a new index pointing to the same file and replays data before verifying that the new index is up to date")]
    public async Task Replay_ShouldSucceed_WhenUsingAnExistingFile()
    {
        //setup first index
        await using var indexFile = new TemporaryFile();
        await using var index1 = new Index(indexFile.FileName);
        
        //store values to first index
        await index1.AdvanceAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        await index1.AdvanceAsync(IndexKey.WritePosition, 456, CancellationToken.None);
        await index1.AdvanceAsync(IndexKey.ReadPosition, 789, CancellationToken.None);
        
        //setup and replay second index
        await using var index2 = new Index(indexFile.FileName);

        await index2.ReplayAsync(CancellationToken.None);
        
        //fetch current values (replayed) from second index
        var readPosition = await index2.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
        var writePosition = await index2.GetOrDefaultAsync(IndexKey.WritePosition, 0, CancellationToken.None);
        
        //assertions
        Assert.That(readPosition, Is.EqualTo(789));
        Assert.That(writePosition, Is.EqualTo(456));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes enough data to an index to trigger a snaphot creation then verifies the result")]
    public async Task Snapshot_ShouldRunAndSucceed_WhenStoringMoreDataThanMaxSize()
    {
        //setup first index
        await using var indexFile = new TemporaryFile();
        await using var index = new Index(indexFile.FileName, 55);
        
        //store values to index
        await index.AdvanceAsync(IndexKey.ReadPosition, 123, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.WritePosition, 456, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.ReadPosition, 789, CancellationToken.None);
        await index.AdvanceAsync(IndexKey.ReadPosition, 999, CancellationToken.None); //this write triggers the snapshot
        
        //wait to give the snaphot task time to perform the snapshot
        await Task.Delay(100);
        
        //fetch the stored value from the second replayed index
        var readPosition = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
        var writePosition = await index.GetOrDefaultAsync(IndexKey.WritePosition, 0, CancellationToken.None);
        
        //assertions
        Assert.That(index.Reader.Position, Is.EqualTo(0));
        Assert.That(index.Reader.Length, Is.EqualTo(36));
        Assert.That(index.Writer.Position, Is.EqualTo(36));
        Assert.That(index.Writer.Length, Is.EqualTo(36));
        Assert.That(readPosition, Is.EqualTo(999));
        Assert.That(writePosition, Is.EqualTo(456));
    }
}