using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;


namespace Benchmark;

[MemoryDiagnoser]
public class ParametrizedBenchmark
{
    [Params(100, 1000)]
    public int Param1 { get; set; }
    
    [Benchmark]
    public async Task FooBarAsync()
        => await Task.CompletedTask;
}