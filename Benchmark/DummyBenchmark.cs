using Benchmark.Common;
using BenchmarkDotNet.Attributes;
using HackyMessage.Core;
using HackyMessage.Core.Partition;
using HackyMessage.Core.Policy.Buffer;
using HackyMessage.Core.Policy.Execution;
using HackyMessage.Core.Queue.DeadLetterQueue;
using HackyMessage.Persistence.Provider;
using HackyMessage.Persistence.Provider.Factory;

namespace Benchmark;

[MemoryDiagnoser]
public class DummyBenchmark
{
    [Benchmark]
    public async Task Wait10MsAsync()
        => await Task.Delay(10);
}