using BenchmarkDotNet.Attributes;


namespace Benchmark;

[MemoryDiagnoser]
public class BaselineMethodBenchmark
{
    [Benchmark(Baseline = true)]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
    
    [Benchmark]
    public async Task Wait20MsAsync()
        => await Task.Delay(20);
}