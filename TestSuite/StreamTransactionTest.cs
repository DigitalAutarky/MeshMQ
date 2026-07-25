using HackyMessage.Persistence;
using HackyMessage.Pooled.Pool;
using TestSuite.Common;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class StreamTransactionTest
{
    private readonly byte[] _magicHeader = [0xD0, 0x0D, 0xFE, 0xED];
    
    [Test]
    [Category("Integration")]
    [Description("Reads some data from a read-only stream and commits the the new offsets.")]
    public async Task ReadStream_ShouldCommitOffsets_WhenCommitingAfterAReadOperation()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateReadStream(temporaryFile.FileName);
        
        await using var transaction = new StreamTransactionPool().Rent(stream);
        
        //write some data
        await WriteToFile(temporaryFile.FileName, _magicHeader);
        var readLimit = stream.Length;
        
        //read and commit
        var buffer = new byte[_magicHeader.Length];
        await transaction.Stream.ReadExactlyAsync(buffer, readLimit);
        transaction.Stream.Commit();
        transaction.Stream.Rollback(); //this should have no effect
        
        //assertions
        Assert.That(stream.Position, Is.EqualTo(_magicHeader.Length));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Reads some data from a read-only stream and commits the the new offsets.")]
    public async Task ReadStream_ShouldCommitOffsets_WhenCommitingAfterAReadOperationInAnEnclosingScope()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateReadStream(temporaryFile.FileName);

        try
        {
            await using var transaction = new StreamTransactionPool().Rent(stream);

            //write some data
            await WriteToFile(temporaryFile.FileName, _magicHeader);
            var readLimit = stream.Length;
            
            //read and commit
            var buffer = new byte[_magicHeader.Length];
            await transaction.Stream.ReadExactlyAsync(buffer, readLimit);
            transaction.Stream.Commit();

            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(stream.Position, Is.EqualTo(_magicHeader.Length));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Reads some data from a read-only stream then perform a rollback which should reset the position attribute on the stream.")]
    public async Task ReadStream_ShouldRollbackPosition_WhenRollingBackAReadStream()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateReadStream(temporaryFile.FileName);
        
        await using var transaction = new StreamTransactionPool().Rent(stream);
        
        //write some data
        await WriteToFile(temporaryFile.FileName, _magicHeader);
        var readLimit = stream.Length;
        
        //read and commit
        var buffer = new byte[_magicHeader.Length];
        await transaction.Stream.ReadExactlyAsync(buffer, readLimit);
        transaction.Stream.Rollback();
        transaction.Stream.Commit(); //this should have no effect
        
        //assertions
        Assert.That(stream.Position, Is.EqualTo(0));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Reads some data from a read-only stream and does not commit the the new offsets before the transaction is disposed.")]
    public async Task ReadStream_ShouldRollbackPosition_WhenTransactionIsDisposedWithoutCommit()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateReadStream(temporaryFile.FileName);

        try
        {
            await using var transaction = new StreamTransactionPool().Rent(stream);

            //write some data
            await WriteToFile(temporaryFile.FileName, _magicHeader);
            var readLimit = stream.Length;
            
            //read and commit
            var buffer = new byte[_magicHeader.Length];
            await transaction.Stream.ReadExactlyAsync(buffer, readLimit);
            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(stream.Position, Is.EqualTo(0));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes some data to a write enabled stream and commits the the new offsets.")]
    public async Task WriteStream_ShouldCommitOffsets_WhenCommitingAfterAWriteOperation()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateWriteStream(temporaryFile.FileName);
        
        await using var transaction = new StreamTransactionPool().Rent(stream);
        
        //write and commit
        await transaction.Stream.WriteAsync(_magicHeader);
        transaction.Stream.Commit();
        transaction.Stream.Rollback(); //this should have no effect
        
        //assertions
        Assert.That(stream.Position, Is.EqualTo(_magicHeader.Length));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes some data to a write enabled stream and commits the the new offsets.")]
    public async Task WriteStream_ShouldCommitOffsets_WhenCommitingAfterAWriteOperationInAnEnclosingScope()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateWriteStream(temporaryFile.FileName);

        try
        {
            await using var transaction = new StreamTransactionPool().Rent(stream);

            //write and commit
            await transaction.Stream.WriteAsync(_magicHeader);
            transaction.Stream.Commit();

            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(stream.Position, Is.EqualTo(_magicHeader.Length));
        Assert.That(stream.Length, Is.EqualTo(_magicHeader.Length));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes some data to a stream then perform a rollback which should reset the position and length attributes on the stream.")]
    public async Task WriteStream_ShouldRollbackPositionAndLength_WhenRollingBackAWriteStream()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateWriteStream(temporaryFile.FileName);
        
        await using var transaction = new StreamTransactionPool().Rent(stream);
        
        //write and commit
        await transaction.Stream.WriteAsync(_magicHeader);
        transaction.Stream.Rollback();
        transaction.Stream.Commit(); //this should have no effect
        
        //assertions
        Assert.That(stream.Position, Is.EqualTo(0));
        Assert.That(stream.Length, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Integration")]
    [Description("Writes some data to a stream and does not commit the the new offsets before the transaction is disposed.")]
    public async Task WriteStream_ShouldRollbackPositionAndLength_WhenTransactionIsDisposedWithoutCommit()
    {
        //setup
        await using var temporaryFile = new TemporaryFile();
        var stream = CreateReadStream(temporaryFile.FileName);

        try
        {
            await using var transaction = new StreamTransactionPool().Rent(stream);

            //write and commit
            await transaction.Stream.WriteAsync(_magicHeader);
            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(stream.Position, Is.EqualTo(0));
        Assert.That(stream.Length, Is.EqualTo(0));
    }

    private static Stream CreateReadStream(string filename)
    {
        return new FileStream(filename, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Write,
            bufferSize: 4096, FileOptions.Asynchronous);
    }
    
    private static Stream CreateWriteStream(string filename)
    {
        return new FileStream(filename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
    }

    public static async Task WriteToFile(string filename, byte[] byteData)
    {
        await FileAppender.AppendAsync(filename, byteData);
    }
}