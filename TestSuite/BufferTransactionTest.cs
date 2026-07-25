using HackyMessage.Persistence;
using HackyMessage.Pooled;
using HackyMessage.Pooled.Pool;
using TestSuite.Common;

namespace TestSuite;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class BufferTransactionTest
{
    private readonly byte[] _magicHeader = [0xD0, 0x0D, 0xFE, 0xED];
    private readonly byte[] _ultraHeader = [0xD0, 0x0D, 0xFE, 0xED];
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data and commits the new buffer length")]
    public async Task WrittenMemory_ShouldCommitOffset_WhenCommitingAfterAWriteOperation()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);
        
        //commit
        transactionLease.Buffer.Commit();
        transactionLease.Buffer.Rollback(); //this should have no effect
        
        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(_magicHeader.Length));
        Assert.That(bufferLease.Buffer.WrittenMemory.ToArray(), Is.EqualTo(_magicHeader));
        Assert.That(bufferLease.Buffer.WrittenSpan.ToArray(), Is.EqualTo(_magicHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data and commits the new buffer length")]
    public async Task WrittenMemory_ShouldCommitOffset_WhenCommitingAfterAWriteOperationInAnEnclosingScope()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        
        try
        {
            await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);

            //write some data
            WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);

            //commit
            transactionLease.Buffer.Commit();
            transactionLease.Buffer.Rollback(); //this should have no effect

            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(_magicHeader.Length));
        Assert.That(bufferLease.Buffer.WrittenMemory.ToArray(), Is.EqualTo(_magicHeader));
        Assert.That(bufferLease.Buffer.WrittenSpan.ToArray(), Is.EqualTo(_magicHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data to the buffer using the transaction then performs a rollback which should reset the buffer.")]
    public async Task WrittenMemory_ShouldRollback_WhenRollingBackATransactionStream()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);
        
        //commit
        transactionLease.Buffer.Rollback();
        transactionLease.Buffer.Commit(); //this should have no effect
        
        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes to the transaction and does not commit the the new offsets before the transaction is disposed.")]
    public async Task WrittenMemory_ShouldRollback_WhenTransactionIsDisposedWithoutCommit()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();

        try
        {
            await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
            
            //write some data
            WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);
            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data and commits the new buffer length")]
    public async Task WrittenSpan_ShouldCommitOffset_WhenCommitingAfterAWriteOperation()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToSpanBuffer(transactionLease.Buffer, _magicHeader);
        
        //commit
        transactionLease.Buffer.Commit();
        transactionLease.Buffer.Rollback(); //this should have no effect
        
        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(_magicHeader.Length));
        Assert.That(bufferLease.Buffer.WrittenMemory.ToArray(), Is.EqualTo(_magicHeader));
        Assert.That(bufferLease.Buffer.WrittenSpan.ToArray(), Is.EqualTo(_magicHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data and commits the new buffer length")]
    public async Task WrittenSpan_ShouldCommitOffset_WhenCommitingAfterAWriteOperationInAnEnclosingScope()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        
        try
        {
            await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);

            //write some data
            WriteToSpanBuffer(transactionLease.Buffer, _magicHeader);

            //commit
            transactionLease.Buffer.Commit();
            transactionLease.Buffer.Rollback(); //this should have no effect

            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(_magicHeader.Length));
        Assert.That(bufferLease.Buffer.WrittenMemory.ToArray(), Is.EqualTo(_magicHeader));
        Assert.That(bufferLease.Buffer.WrittenSpan.ToArray(), Is.EqualTo(_magicHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes some data to the buffer using the transaction then performs a rollback which should reset the buffer.")]
    public async Task WrittenSpan_ShouldRollback_WhenRollingBackATransactionStream()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToSpanBuffer(transactionLease.Buffer, _magicHeader);
        
        //commit
        transactionLease.Buffer.Rollback();
        transactionLease.Buffer.Commit(); //this should have no effect
        
        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes to the transaction and does not commit the the new offsets before the transaction is disposed.")]
    public async Task WrittenSpan_ShouldRollback_WhenTransactionIsDisposedWithoutCommit()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();

        try
        {
            await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
            
            //write some data
            WriteToSpanBuffer(transactionLease.Buffer, _magicHeader);
            throw new IOException();
        }
        catch (Exception ex)
        {
            //ignore exception
        }

        //assertions
        Assert.That(bufferLease.Buffer.WrittenCount, Is.EqualTo(0));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes data twice and reads the second pending data array back before commit")]
    public async Task Memory_ShouldReadData_WhenCallingGetMemoryWithoutCommit()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);
        var start = transactionLease.Buffer.GetLength();
        WriteToMemoryBuffer(transactionLease.Buffer, _ultraHeader);
        var end = transactionLease.Buffer.GetLength();
        
        //read partial memory data
        var result = transactionLease.Buffer.GetMemory(start, end - start);
        
        //assertions
        Assert.That(result.ToArray(), Is.EqualTo(_ultraHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes data twice and reads the second pending data array back before commit")]
    public async Task Span_ShouldReadData_WhenCallingGetMemoryWithoutCommit()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>().Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write some data
        WriteToSpanBuffer(transactionLease.Buffer, _magicHeader);
        var start = transactionLease.Buffer.GetLength();
        WriteToSpanBuffer(transactionLease.Buffer, _ultraHeader);
        var end = transactionLease.Buffer.GetLength();
        
        //read partial memory data
        var result = transactionLease.Buffer.GetSpan(start, end - start);
        
        //assertions
        Assert.That(result.ToArray(), Is.EqualTo(_ultraHeader));
    }
    
    [Test]
    [Category("Unit")]
    [Description("Writes enough data to force the underlying buffer to resize and ensures commit data isn't lost")]
    public async Task Buffer_ShouldRetainData_WhenForcingUnderlyingBufferToResize()
    {
        //setup
        await using var bufferLease = new ArrayBufferWriterPool<byte>(1, 2).Rent();
        await using var transactionLease = new BufferTransactionPool<byte>().Rent(bufferLease.Buffer);
        
        //write enough data and commit
        WriteToMemoryBuffer(transactionLease.Buffer, _magicHeader);
        WriteToMemoryBuffer(transactionLease.Buffer, _ultraHeader);
        transactionLease.Buffer.Commit();
        
        //read memory data
        var resultHex = Convert.ToHexString(bufferLease.Buffer.WrittenMemory.ToArray());
        
        //compute the expected result
        var expected = new byte [_magicHeader.Length + _ultraHeader.Length];
        _magicHeader.AsSpan().CopyTo(expected);
        _ultraHeader.AsSpan().CopyTo(expected.AsSpan(_magicHeader.Length));
        var expectedHex = Convert.ToHexString(expected);
        
        //assertions
        Assert.That(resultHex, Is.EqualTo(expectedHex));
    }

    private static void WriteToMemoryBuffer(BufferTransaction<byte> transaction, byte[] byteData)
    {
        var memory = transaction.GetMemory(byteData.Length);
        byteData.CopyTo(memory.Span);
        transaction.Advance(byteData.Length);
    }
    
    private static void WriteToSpanBuffer(BufferTransaction<byte> transaction, byte[] byteData)
    {
        var memory = transaction.GetSpan(byteData.Length);
        byteData.CopyTo(memory);
        transaction.Advance(byteData.Length);
    }
}