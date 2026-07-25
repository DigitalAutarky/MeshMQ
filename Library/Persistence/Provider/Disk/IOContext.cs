using HackyMessage.Common;
using HackyMessage.Persistence.Provider.Disk.Index;

namespace HackyMessage.Persistence.Provider.Disk;

public readonly record struct IoContext (
    Stream LogWriter,
    Stream LogReader,
    IMyAsyncLock WriteLock,
    IMyAsyncLock ReadLock,
    IIndexProvider Index,
    long highWatermark,
    long lowWatermark,
    long MaxSize
) : IDisposable, IAsyncDisposable
{
    public void Dispose()
    {
        LogWriter.Dispose();
        LogReader.Dispose();
        WriteLock.Dispose();
        Index.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await LogWriter.DisposeAsync();
        await LogReader.DisposeAsync();
        await WriteLock.DisposeAsync();
        await Index.DisposeAsync();
    }
}