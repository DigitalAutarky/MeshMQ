using BenchmarkDotNet.Attributes;


namespace Benchmark;

[MemoryDiagnoser]
public class UnchangedMethodBenchmark
{
    [Benchmark]
    public async Task DoNothingAsync()
        => await Task.CompletedTask;
}