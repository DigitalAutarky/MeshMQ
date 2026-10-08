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
        public BenchmarkCell[] Cells { get; set; } = [];
    }

    public class BenchmarkTopChangeItem
    {
        public string Type { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string Attribute { get; set; } = string.Empty;
        public string Ratio { get; set; } = string.Empty;
        public bool IsRegression { get; set; }
        public bool IsImprovement { get; set; }
    }

    public class BenchmarkGroup
    {
        public string GroupKey { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public bool HasRegressions { get; set; }
        public bool HasImprovements { get; set; }
        public string[] Headers { get; set; } = [];
        public BenchmarkRow[] Rows { get; set; } = [];
        public BenchmarkTopChangeItem TopChangeItem { get; set; }
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
        public BenchmarkEnvironment[] Environment { get; set; } = [];
        public BenchmarkGroup[] Groups { get; set; } = [];
    }
}