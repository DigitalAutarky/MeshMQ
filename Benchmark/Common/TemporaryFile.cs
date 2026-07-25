namespace Benchmark.Common;

public class TemporaryFile: IDisposable, IAsyncDisposable
{
    public string FileName { get; } = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(FileName)) File.Delete(FileName);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
    }
}