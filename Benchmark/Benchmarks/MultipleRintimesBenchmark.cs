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
    public async Task Wait30MsAsync()
        => await Task.Delay(30);
}