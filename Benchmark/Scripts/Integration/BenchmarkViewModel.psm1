class BenchmarkCell {
    [string]$Text
    [bool]$IsBest
    [bool]$IsRegression
    [bool]$IsImprovement
}

class BenchmarkRow {
    [BenchmarkCell[]]$Cells
}

class BenchmarkGroup {
    [string]$GroupName
    [bool]$HasRegressions
    [bool]$HasImprovements
    [string[]]$Headers
    [BenchmarkRow[]]$Rows
}

class BenchmarkEnvironment {
    [string]$Label
    [string]$Current
    [string]$Baseline
    [bool]$HasChanged
}

class BenchmarkViewModel {
    [bool]$OverallFailure
    [bool]$IsComparingAgainstSelf
    [BenchmarkEnvironment[]]$Environment
    [BenchmarkGroup[]]$Groups
}