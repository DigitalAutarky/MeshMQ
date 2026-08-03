[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$BenchmarkPath,

    [Parameter(Mandatory=$true)]
    [string]$BaselinePath,

    [Parameter(Mandatory=$true)]
    [string]$ComparisonResult,

    [Parameter(Mandatory=$true)]
    [string[]]$Display
)

# 1. Assert exactly 1 benchmark file and 1 baseline file
$benchFiles = Get-ChildItem -Path $BenchmarkPath -Filter "*-report-full.json"
if ($benchFiles.Count -ne 1) {
    Write-Error "Expected exactly 1 benchmark JSON file in '$BenchmarkPath', found $($benchFiles.Count)."
    exit 1
}

$baseFiles = Get-ChildItem -Path $BaselinePath -Filter "*-report-full.json"
if ($baseFiles.Count -ne 1) {
    Write-Error "Expected exactly 1 baseline JSON file in '$BaselinePath', found $($baseFiles.Count)."
    exit 1
}

# 2. Parse the benchmark and baseline JSONs into objects
$benchJson = Get-Content -Path $benchFiles[0].FullName | ConvertFrom-Json
$baseJson = Get-Content -Path $baseFiles[0].FullName | ConvertFrom-Json

# Helper: Parse the Display argument strings into structured objects
$displayCols = foreach ($d in $Display) {
    $dict = @{}
    $d -split '&' | ForEach-Object {
        $kv = $_ -split '='
        $dict[$kv[0].ToLower()] = $kv[1]
    }
    [PSCustomObject]@{
        Key = $dict['key']
        Name = $dict['name']
        Threshold = [double]$dict['threshold']
    }
}

# Helper: Safely extract nested JSON properties (e.g., "Statistics.Mean")
function Get-NestedProperty {
    param($obj, [string]$path)
    if ($null -eq $obj) { return $null }
    $current = $obj
    foreach ($part in $path.Split('.')) {
        if ($null -eq $current) { return $null }
        $current = $current.$part
    }
    return $current
}

# 3. Create a sorted list of benchmarks by FullName descending
$sortedBenchmarks = $benchJson.Benchmarks | Sort-Object FullName -Descending

$md = [System.Text.StringBuilder]::new()

# 4. Write title into Markdown file
$md.AppendLine("# Benchmark Summary") | Out-Null
$md.AppendLine() | Out-Null

$overallFailure = $false

# Group benchmarks by their Type (Class name)
$groupedBenchmarks = $sortedBenchmarks | Group-Object Type

foreach ($group in $groupedBenchmarks) {
    # 6. Write the type as a smaller header
    $md.AppendLine("## $($group.Name)") | Out-Null

    # Construct Markdown Table Header
    $headerNames = $displayCols.Name -join " | "
    $md.AppendLine("| MethodTitle | Parameters | $headerNames |") | Out-Null

    # Construct Markdown Table Separator
    $separators = $displayCols | ForEach-Object { "---" }
    $md.AppendLine("|---|---|$(($separators -join '|'))|") | Out-Null

    # 5. Iterate through the sorted list
    foreach ($bench in $group.Group) {

        # Find matching baseline using FullName
        $baseline = $baseJson.Benchmarks | Where-Object FullName -eq $bench.FullName | Select-Object -First 1

        $rowCells = [System.Collections.Generic.List[string]]::new()

        $methodTitle = if ($bench.MethodTitle) { $bench.MethodTitle } else { "N/A" }
        $parameters = if ($bench.Parameters) { $bench.Parameters } else { "None" }

        # Markdown tables break if there are pipe '|' characters in parameters, so replacing them just in case
        $rowCells.Add($methodTitle.Replace('|', '-'))
        $rowCells.Add($parameters.Replace('|', '-'))

        foreach ($col in $displayCols) {
            $currentVal = Get-NestedProperty -obj $bench -path $col.Key
            $baseVal = Get-NestedProperty -obj $baseline -path $col.Key

            $cellText = "N/A"
            $isFailed = $false

            if ($null -ne $currentVal) {
                # Format raw numbers cleanly to 2 decimal places
                $currentFmt = "{0:N2}" -f $currentVal

                if ($null -ne $baseVal -and $baseVal -ne 0) {
                    # 7. Calculate ratio and append in brackets
                    $ratio = $currentVal / $baseVal
                    $ratioStr = "{0:N2}" -f $ratio

                    if ($ratio -gt 1) {
                        $ratioStr = "+$ratioStr"
                    }

                    $cellText = "$currentFmt ($ratioStr)"

                    # 8 & 9. Check Threshold limits
                    if ($col.Threshold -gt 1 -and $ratio -gt $col.Threshold) {
                        $isFailed = $true
                    } elseif ($col.Threshold -lt 1 -and $ratio -lt $col.Threshold) {
                        $isFailed = $true
                    }
                } else {
                    $cellText = $currentFmt
                }
            }

            if ($isFailed) {
                $overallFailure = $true
                # Attempt standard HTML red output, fallback to bold and emoji
                $cellText = "**<span style=`"color:red`">$cellText</span>** 🔴"
            }

            $rowCells.Add($cellText)
        }

        # Write row to Markdown
        $md.AppendLine("| $(($rowCells -join ' | ')) |") | Out-Null
    }
    $md.AppendLine() | Out-Null
}

# Ensure destination directory exists before writing
$outDir = Split-Path $ComparisonResult -Parent
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Output to Markdown file
$md.ToString() | Set-Content -Path $ComparisonResult -Encoding UTF8

if ($overallFailure) {
    Write-Error "Performance regression detected! One or more benchmarks exceeded their configured thresholds."
    exit 1
}