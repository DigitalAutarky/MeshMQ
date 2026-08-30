using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;

namespace Benchmark;
using BenchmarkDotNet.Running;

internal static class Program
{
    public static void Main(string[] args)
    {
        // 1. Setup configuration
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.JoinSummary)
            .AddValidator(new StrictRuntimeValidator())
            .AddExporter(new AugmentedJsonExporter());

        // 2. Run benchmarks
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
            .Run(args, config);
    }
}