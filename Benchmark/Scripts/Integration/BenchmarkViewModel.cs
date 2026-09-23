namespace Benchmark
{
    public class BenchmarkCell
    {
        public string Text { get; set; } = string.Empty;
        public bool IsBest { get; set; }
        public bool IsRegression { get; set; }
        public bool IsImprovement { get; set; }
    }

    public class BenchmarkRow
    {
        public BenchmarkCell[] Cells { get; set; } = System.Array.Empty<BenchmarkCell>();
    }

    public class BenchmarkGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public bool HasRegressions { get; set; }
        public bool HasImprovements { get; set; }
        public string[] Headers { get; set; } = System.Array.Empty<string>();
        public BenchmarkRow[] Rows { get; set; } = System.Array.Empty<BenchmarkRow>();
    }

    public class BenchmarkEnvironment
    {
        public string Label { get; set; } = string.Empty;
        public string Current { get; set; } = string.Empty;
        public string Baseline { get; set; } = string.Empty;
        public bool HasChanged { get; set; }
    }

    public class BenchmarkViewModel
    {
        public bool OverallFailure { get; set; }
        public bool IsComparingAgainstSelf { get; set; }
        public BenchmarkEnvironment[] Environment { get; set; } = System.Array.Empty<BenchmarkEnvironment>();
        public BenchmarkGroup[] Groups { get; set; } = System.Array.Empty<BenchmarkGroup>();
    }
}