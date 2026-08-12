using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Benchmark;

[SimpleJob(RuntimeMoniker.NetCoreApp30, baseline: true)]
[SimpleJob(RuntimeMoniker.Mono)]
[MemoryDiagnoser]
public class MultipleRuntimesBenchmark
{
    [Benchmark]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
    
    [Benchmark]
    public async Task Wait20MsAsync()
        => await Task.Delay(20);
}