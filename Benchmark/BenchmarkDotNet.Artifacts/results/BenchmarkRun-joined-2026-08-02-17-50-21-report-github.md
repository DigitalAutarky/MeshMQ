```

BenchmarkDotNet v0.16.0-preview.1, Linux Linux Mint 20.3 (Una)
Intel Core i7-8650U CPU 1.90GHz (Max: 2.70GHz) (Kaby Lake R), 1 CPU, 8 logical and 4 physical cores
Memory: 15.37 GB Total, 0.41 GB Available
.NET SDK 11.0.100-preview.4.26230.115
  [Host]     : .NET 11.0.0 (11.0.0-preview.4.26230.115, 11.0.26.23115), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 11.0.0 (11.0.0-preview.4.26230.115, 11.0.26.23115), X64 RyuJIT x86-64-v3
  Job-CNUJVU : .NET 11.0.0 (11.0.0-preview.4.26230.115, 11.0.26.23115), X64 RyuJIT x86-64-v3


```
| Type               | Method                          | Job        | InvocationCount | UnrollFactor | WriteBufferSize | ReadBatchSize | Mean        | Error     | StdDev    | Median      | Gen0      | Gen1      | Allocated  |
|------------------- |-------------------------------- |----------- |---------------- |------------- |---------------- |-------------- |------------:|----------:|----------:|------------:|----------:|----------:|-----------:|
| **DummyBenchmark**     | **Wait10MsAsync**                   | **DefaultJob** | **Default**         | **16**           | **?**               | **?**             |    **11.98 ms** |  **0.017 ms** |  **0.016 ms** |    **11.98 ms** |         **-** |         **-** |      **272 B** |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **100**             | **100**           | **1,231.95 ms** | **12.299 ms** | **11.504 ms** | **1,228.82 ms** | **1000.0000** |         **-** | **11890344 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 100             | 100           |    11.53 ms |  0.364 ms |  1.075 ms |    12.24 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **100**             | **1000**          |   **895.16 ms** | **13.258 ms** | **11.753 ms** |   **894.71 ms** | **1000.0000** |         **-** | **11753960 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 100             | 1000          |    11.38 ms |  0.368 ms |  1.085 ms |    12.21 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **100**             | **10000**         |   **864.88 ms** | **10.997 ms** | **10.286 ms** |   **862.55 ms** | **1000.0000** |         **-** | **11708752 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 100             | 10000         |    10.27 ms |  0.078 ms |  0.065 ms |    10.26 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **1000**            | **100**           |   **666.25 ms** | **12.825 ms** | **13.170 ms** |   **667.83 ms** | **1000.0000** |         **-** | **11564904 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 1000            | 100           |    11.39 ms |  0.369 ms |  1.087 ms |    12.22 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **1000**            | **1000**          |   **162.24 ms** |  **9.094 ms** | **26.239 ms** |   **161.74 ms** | **1000.0000** |         **-** | **10965976 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 1000            | 1000          |    11.21 ms |  0.385 ms |  1.134 ms |    10.36 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **1000**            | **10000**         |   **113.16 ms** |  **3.550 ms** | **10.468 ms** |   **115.21 ms** | **1000.0000** |         **-** | **10929072 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 1000            | 10000         |    11.55 ms |  0.344 ms |  1.016 ms |    12.23 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **10000**           | **100**           |   **659.17 ms** | **13.179 ms** | **34.718 ms** |   **668.54 ms** | **2000.0000** | **1000.0000** | **13508008 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 10000           | 100           |    11.32 ms |  0.385 ms |  1.135 ms |    12.22 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **10000**           | **1000**          |   **157.02 ms** |  **7.303 ms** | **21.535 ms** |   **156.71 ms** | **1000.0000** |         **-** | **13294016 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 10000           | 1000          |    11.20 ms |  0.406 ms |  1.192 ms |    10.35 ms |         - |         - |      768 B |
| **PartitionBenchmark** | **EndToEndBenchmark**               | **Job-CNUJVU** | **1**               | **1**            | **10000**           | **10000**         |    **85.82 ms** |  **3.512 ms** | **10.133 ms** |    **83.22 ms** | **1000.0000** |         **-** | **13307336 B** |
| PartitionBenchmark | TestMultipleBenchmarksSameClass | Job-CNUJVU | 1               | 1            | 10000           | 10000         |    11.14 ms |  0.407 ms |  1.201 ms |    10.33 ms |         - |         - |      768 B |
