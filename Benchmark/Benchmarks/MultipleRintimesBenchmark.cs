using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Benchmark;

[MemoryDiagnoser]
[Config(typeof(AllNet11PreviewsConfig))]
public class MultipleRuntimesBenchmark
{
    [Params(100, 1000)]
    public int Param1 { get; set; }
    
    [Benchmark]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
}