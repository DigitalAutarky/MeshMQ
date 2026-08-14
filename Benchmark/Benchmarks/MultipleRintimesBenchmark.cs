using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Benchmark;

[MemoryDiagnoser]
[Config(typeof(AllNet11PreviewsConfig))]
public class MultipleRuntimesBenchmark
{
    [Benchmark]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
}