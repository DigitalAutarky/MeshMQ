namespace HackyMessage.Persistence.Provider.Disk.Index;

public interface IIndexProvider : IDisposable, IAsyncDisposable
{
    Task AdvanceAsync(short key, long value, CancellationToken ct = default);
    Task<long> GetOrDefaultAsync(short key, long defaultValue, CancellationToken ct = default);
    Task ReplayAsync(CancellationToken ct =  default);
}