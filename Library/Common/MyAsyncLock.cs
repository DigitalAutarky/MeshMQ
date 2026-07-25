using DotNext.Threading;

namespace HackyMessage.Common;

public interface IMyAsyncLock : IDisposable, IAsyncDisposable
{
    ValueTask<AsyncLock.Scope> AcquireAsync(TimeSpan timeout, CancellationToken ct = default);
}

public class MyAsyncLock: IMyAsyncLock
{
    private readonly AsyncLock _lock = AsyncLock.Exclusive();

    public async ValueTask<AsyncLock.Scope> AcquireAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        return await _lock.TryAcquireAsync(timeout, ct);
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.DisposeAsync();
    }
}