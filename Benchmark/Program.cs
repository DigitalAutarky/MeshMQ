using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Validators;

namespace Benchmark;
using BenchmarkDotNet.Running;

internal static class Program
{
    public static void Main(string[] args)
    {
        // 1. Setup configuration
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddValidator(new StrictRuntimeValidator())
            .AddValidator(ExecutionValidator.FailOnError)
            .AddExporter(new AugmentedJsonExporter());

        // 2. Run benchmarks
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
            .Run(args, config);
    }
}