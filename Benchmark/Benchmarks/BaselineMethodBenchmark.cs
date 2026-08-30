using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;


namespace Benchmark;

[MemoryDiagnoser]
//[SimpleJob(RuntimeMoniker.Net10_0)]
public class BaselineMethodBenchmark
{
    [Params(100, 1000)]
    public int Param1 { get; set; }
    
    [Benchmark(Baseline = true)]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
    
    [Benchmark]
    public async Task Wait20MsAsync()
        => await Task.Delay(20);
}