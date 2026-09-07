using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace Benchmark;

public class AugmentedJsonExporter : IExporter
{
    public string Name => "AugmentedFullJson";
    public void ExportToLog(Summary summary, ILogger logger) { }

    public async ValueTask ExportAsync(Summary summary, ILogger logger, CancellationToken cancellationToken)
    {
        // 1. Run the base JSON exporter
        await ((IExporter)JsonExporter.Full).ExportAsync(summary, logger, cancellationToken);

        // 2. Safely get the exact file path without hardcoded magic strings
        // (GetArtifactFullName became public in BDN v0.15.1)
        var baseExporter = (ExporterBase)JsonExporter.Full;
        var originalFilePath = Path.GetFullPath(baseExporter.GetArtifactFullName(summary));

        // Early return flattens the scope and eliminates nesting
        if (!File.Exists(originalFilePath))
        {
            logger.WriteLineError($"Could not find the generated JSON file at: {originalFilePath}");
            return;
        }

        // 3. Read JSON to be augmented
        var jsonContent = await File.ReadAllTextAsync(originalFilePath, cancellationToken);
        var jsonRoot = JsonNode.Parse(jsonContent)!.AsObject();
        var jsonBenchmarks = jsonRoot["Benchmarks"]!.AsArray();
        
        // Retrieve benchmark cases in BDN's exact summary output order
        var orderedCases = summary.Orderer.GetSummaryOrder(summary.BenchmarksCases, summary);

        //Group by BDN's active logical group key (respects [GroupBenchmarksBy] and custom IGroupingOrderer)
        var bdnGroups = orderedCases
            .GroupBy(bCase => summary.Orderer.GetLogicalGroupKey(summary.BenchmarksCases, bCase));
        
        // 4. Augment the JSON
        var groupKey = 0;
        foreach (var group in bdnGroups)
        {
            groupKey++;
            
            // Map BenchmarkCases back to their respective BenchmarkReports
            var groupReports = group.Select(bCase => summary[bCase])
                .Where(report => report != null);

            var sortKey = 0;
            foreach (var report in groupReports)
            {
                sortKey++;
                
                var targetKey = report.BenchmarkCase.DisplayInfo;
                var jsonNode = jsonBenchmarks.FirstOrDefault(n => n?["DisplayInfo"]?.ToString() == targetKey);
                if (jsonNode == null) continue;
            
                var jsonBenchmark = jsonNode.AsObject();
                AddCategory(jsonBenchmark, report);
                AddJobId(jsonBenchmark, report);
                AddExplicitRuntime(jsonBenchmark, report, summary);
                AddBaselineDescriptor(jsonBenchmark, report);
                AddGroupAndSortKeys(jsonBenchmark, groupKey, sortKey);
            }
        }

        // 5. Generate a dynamic filename based entirely on the original
        var directory = Path.GetDirectoryName(originalFilePath)!;
        var fileName = Path.GetFileNameWithoutExtension(originalFilePath);
        var newFilePath = Path.Combine(directory, $"{fileName}-augmented.json");
        
        // 6. Save the new file
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(newFilePath, jsonRoot.ToJsonString(options), cancellationToken);

        // 7. Clean up the default file so you only keep your augmented one
        File.Delete(originalFilePath);
        logger.WriteLineInfo($"Exported augmented JSON to: {newFilePath}");
    }

    private static void AddGroupAndSortKeys(JsonObject jsonBenchmark, int groupKey, int sortKey)
    {
        jsonBenchmark["GroupingKey"] = groupKey;
        jsonBenchmark["SortingKey"] = sortKey;
    }

    private static void AddCategory(JsonObject jsonBenchmark, BenchmarkReport report)
    {
        //benchmark dotnet groups by the exact combination of tags so we can combine them
        var categories = JsonSerializer.SerializeToNode(report.BenchmarkCase.Descriptor.Categories);
        jsonBenchmark["Categories"] = categories;
    }
    
    private static void AddJobId(JsonObject jsonBenchmark, BenchmarkReport report)
    {
        var jobId = report.BenchmarkCase.Job.Id;
        jsonBenchmark["JobId"] = jobId;
    }
    
    private static void AddExplicitRuntime(JsonObject jsonBenchmark, BenchmarkReport report, Summary summary)
    {
        var jobId = report.BenchmarkCase.Job.Id;
        var runtimeName = (string.IsNullOrEmpty(jobId) || jobId == "Default")
            ? report.BenchmarkCase.Job.Environment.Runtime?.Name ?? summary.HostEnvironmentInfo.RuntimeVersion
            : jobId;
    
        jsonBenchmark["RuntimeName"] = runtimeName;
    }

    private static void AddBaselineDescriptor(JsonObject jsonBenchmark, BenchmarkReport report)
    {
        var isBaseline = report.BenchmarkCase.Descriptor.Baseline;
        jsonBenchmark["IsBaseline"] = isBaseline;
    }

    private static void AddRatios(JsonObject jsonBenchmark, BenchmarkReport report, Summary summary)
    {
        var currentCase = report.BenchmarkCase;

        // Find the baseline for this exact environment/job configuration
        var baselineCase = summary.BenchmarksCases
            .FirstOrDefault(c => c.Descriptor.Baseline &&
                                 c.Job.DisplayInfo == currentCase.Job.DisplayInfo &&
                                 c.Parameters.DisplayInfo == currentCase.Parameters.DisplayInfo);

        if (baselineCase == null || !summary.HasReport(baselineCase))
        {
            SetRatios(jsonBenchmark, null, null);
            return;
        }

        var baselineReport = summary[baselineCase];
                
        // Ensure both have valid statistics to avoid null references
        if (report.ResultStatistics == null || baselineReport.ResultStatistics == null)
        {
            SetRatios(jsonBenchmark, null, null);
            return;
        }
        
        var currentMean = report.ResultStatistics.Mean;
        var baselineMean = baselineReport.ResultStatistics.Mean;
        var timeRatio = currentMean / baselineMean;
        
        var currentMemory = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase);
        var baselineMemory = baselineReport.GcStats.GetBytesAllocatedPerOperation(baselineReport.BenchmarkCase);
        var memoryRatio  = currentMemory / baselineMemory;
        
        SetRatios(jsonBenchmark, timeRatio, memoryRatio);
    }
    

    private static void SetRatios(JsonObject jsonBenchmark, double? timeRatio, double? memoryRatio)
    {
        var timeValue = timeRatio != null ? timeRatio.ToString() : "";
        jsonBenchmark["TimeRatio"] = timeValue;
        
        var memoryValue = memoryRatio != null ? memoryRatio.ToString() : "";
        jsonBenchmark["MemoryRatio"] = memoryValue;
    }
}