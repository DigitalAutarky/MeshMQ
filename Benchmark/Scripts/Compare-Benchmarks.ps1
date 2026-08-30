[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$BenchmarkPath,

    [Parameter(Mandatory=$true)]
    [string]$BaselinePath,

    [Parameter(Mandatory=$true)]
    [string]$ComparisonResult,

    [Parameter(Mandatory=$true)]
    [bool]$FailOnRegression,

    [Parameter(Mandatory=$true)]
    [string]$CommentTag,

    [Parameter(Mandatory=$true)]
    [string[]]$Display
)

# 0. Imports
Import-Module "$PSScriptRoot/Conversion.psm1" -Force
Import-Module "$PSScriptRoot/Format.psm1" -Force

# 1. Assert exactly 1 benchmark file and 1 baseline file
$benchFiles = Get-ChildItem -Path $BenchmarkPath -Filter "*-report-full-augmented.json"
if ($benchFiles.Count -ne 1) {
    Write-Error "Expected exactly 1 benchmark JSON file in '$BenchmarkPath', found $($benchFiles.Count)."
    exit 1
}

$isComparingAgainstSelf = $false
$baseFiles = Get-ChildItem -Path $BaselinePath -Filter "*-report-full-augmented.json"
if ($baseFiles.Count -eq 0) {
    Write-Warning "No baseline JSON files found. If this is your first pull request you can ignore this warning."
    $baseFiles = $benchFiles # compare benchmark against itself on the first pull request
    $isComparingAgainstSelf = $true
} elseif ($baseFiles.Count -ne 1) {
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
        Unit = $dict['unit']
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

# Helper: Parse BenchmarkDotNet Parameter String into a Dictionary
function Get-ParsedParameters {
    param([string]$ParamString)
    $dict = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($ParamString) -and $ParamString -ne "None") {
        $parts = $ParamString -split '&'
        foreach ($part in $parts) {
            $kv = $part -split '=', 2
            if ($kv.Count -eq 2) {
                $key = [uri]::UnescapeDataString($kv[0].Replace("+", " ").Trim())
                $val = [uri]::UnescapeDataString($kv[1].Replace("+", " ").Trim())
                $dict[$key] = $val
            } else {
                $key = "Param_$($dict.Count)"
                $val = [uri]::UnescapeDataString($part.Replace("+", " ").Trim())
                $dict[$key] = $val
            }
        }
    }
    return $dict
}

function Render-ExecutionContext {
    param([System.Text.StringBuilder]$md, [PSCustomObject]$bench, [PSCustomObject]$base)

    $md.AppendLine("> <div align=""center"">") | Out-Null
    $md.AppendLine("> ") | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.BenchmarkDotNetCaption" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.BenchmarkDotNetVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.OsVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.ProcessorName" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.RuntimeVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.Configuration" -bench $bench -base $base | Out-Null
    $md.AppendLine("> ") | Out-Null
    $md.AppendLine("> </div>") | Out-Null
    $md.AppendLine("___") | Out-Null
}

function Render-ExecutionContext-Element {
    param([System.Text.StringBuilder]$md, [System.String]$key, [PSCustomObject]$bench, [PSCustomObject]$base)
    $benchValue = $bench
    $baseValue = $base

    foreach ($part in $key.Split('.')) {
        if ($null -ne $benchValue) {$benchValue = $benchValue.$part }
        if ($null -ne $baseValue) {$baseValue = $baseValue.$part }
    }

    $result = "$benchValue"
    if($benchValue -ne $baseValue) {
        $result = "$\color{orange}{\mathbf{\text{$result (was: $baseValue)}}}$"
    }

    $result = "> $result"
    $md.AppendLine($result) | Out-Null
}

# 3. Create a sorted list of benchmarks by FullName descending
$sortedBenchmarks = $benchJson.Benchmarks | Sort-Object FullName -Descending

$md = [System.Text.StringBuilder]::new()

# 4. Write title and comment anchor tag into Markdown file
$md.AppendLine("<!-- tag:$CommentTag -->") | Out-Null

# FIX (a): Render Warning callout at root Markdown level (outside <details>)
if ($isComparingAgainstSelf) {
    $md.AppendLine("> [!WARNING]") | Out-Null
    $md.AppendLine("> No baseline JSON found so this run compared the benchmark result against itself.") | Out-Null
    $md.AppendLine("> If this is your first run using this action it is the expected result and can be ignored.") | Out-Null
    $md.AppendLine("> If This is not your first run then something is wrong with your workflow.") | Out-Null
    $md.AppendLine("") | Out-Null
}

$md.AppendLine("<details>") | Out-Null
$md.AppendLine("<summary>") | Out-Null
$md.AppendLine("") | Out-Null
$md.AppendLine("## Benchmark Summary {{STATUS_EMOJI}}") | Out-Null
$md.AppendLine("") | Out-Null
$md.AppendLine("</summary>") | Out-Null
$md.AppendLine("") | Out-Null
$md.AppendLine("") | Out-Null

Render-ExecutionContext -md $md -bench $benchJson -base $baseJson | Out-Null

$md.AppendLine("") | Out-Null
$md.AppendLine("") | Out-Null

$overallFailure = $false

# FIX (d & e): Group benchmarks by Type (Benchmark Class) instead of LogicalGroupKey
$groupedBenchmarks = $sortedBenchmarks | Group-Object Type

foreach ($group in $groupedBenchmarks) {
    $groupKey = $group.Name

    $hasRegressions = $false
    $hasImprovements = $false

    # Write the Header format
    $md.AppendLine("<details>") | Out-Null
    $md.AppendLine("<summary>") | Out-Null
    $md.AppendLine("") | Out-Null
    $md.AppendLine("### $groupKey {{BENCH_HASREGRESSIONS}} {{BENCH_HASIMPROVEMENTS}}") | Out-Null
    $md.AppendLine("") | Out-Null
    $md.AppendLine("</summary>") | Out-Null

    # Gather all unique parameters for this group
    $allParamKeys = [System.Collections.Generic.List[string]]::new()
    $groupParamsMap = @{}

    foreach ($bench in $group.Group) {
        $pString = if ($bench.Parameters) { $bench.Parameters } else { "" }
        $parsedParams = Get-ParsedParameters -ParamString $pString
        $groupParamsMap[$bench.FullName] = $parsedParams

        foreach ($key in $parsedParams.Keys) {
            if ($key -notin $allParamKeys) {
                $allParamKeys.Add($key)
            }
        }
    }

    # Construct Standard Markdown Table Headers
    $headerCells = [System.Collections.Generic.List[string]]::new()
    $separatorCells = [System.Collections.Generic.List[string]]::new()

    # FIX (b): Always include "Method" as the first column for every group
    $headerCells.Add("Method")
    $separatorCells.Add(":---")

    # Add dynamic parameter headers
    foreach ($k in $allParamKeys) {
        $headerCells.Add($k)
        $separatorCells.Add(":---")
    }

    # Add display column headers
    foreach ($col in $displayCols) {
        $headerCells.Add($col.Name)
        $separatorCells.Add("---:")
    }

    # Write table structure
    $md.AppendLine()
    $md.AppendLine("| $(($headerCells -join ' | ')) |") | Out-Null
    $md.AppendLine("| $(($separatorCells -join ' | ')) |") | Out-Null

    # Determine optimal units and best values
    $bestValues = @{}
    $optimalUnits = @{}
    $unitTypes = @{}

    foreach ($col in $displayCols) {
        $validValues = @($group.Group | ForEach-Object { Get-NestedProperty -obj $_ -path $col.Key } | Where-Object { $null -ne $_ })

        if ($validValues.Count -gt 0) {
            $optimalUnits[$col.Key] = Get-Optimal-DisplayUnit -Values ([double[]]$validValues) -Unit $col.Unit
            $unitTypes[$col.Key] = Get-Unit-Type -Unit $optimalUnits[$col.Key]

            if ($group.Group.Count -gt 1) {
                if ($col.Threshold -gt 1) {
                    $bestValues[$col.Key] = ($validValues | Measure-Object -Minimum).Minimum
                } elseif ($col.Threshold -lt 1) {
                    $bestValues[$col.Key] = ($validValues | Measure-Object -Maximum).Maximum
                }
            }
        }
    }

    # Iterate through the benchmarks in the group
    foreach ($bench in $group.Group) {
        $baseline = $baseJson.Benchmarks | Where-Object FullName -eq $bench.FullName | Select-Object -First 1

        $rowCells = [System.Collections.Generic.List[string]]::new()

        # FIX (b): Render MethodTitle in the first cell of every row
        $methodTitleVal = if ($bench.MethodTitle) { $bench.MethodTitle.Replace('|', '-') } else { "N/A" }
        $rowCells.Add($methodTitleVal)

        # Render dynamic parameter cells
        $pDict = $groupParamsMap[$bench.FullName]
        foreach ($k in $allParamKeys) {
            $val = if ($pDict.Contains($k)) { $pDict[$k] } else { "N/A" }
            $rowCells.Add($val.Replace('|', '-'))
        }

        # Render statistical display cells with inline Ratio
        foreach ($col in $displayCols) {
            $currentVal = Get-NestedProperty -obj $bench -path $col.Key
            $baseVal = Get-NestedProperty -obj $baseline -path $col.Key

            $cellText = "N/A"
            $isFailed = $false
            $isImproved = $false

            if ($null -ne $currentVal) {
                $optUnit = $optimalUnits[$col.Key]
                $uType = $unitTypes[$col.Key]

                $convertedCurrent = Convert -Value $currentVal -FromUnit $col.Unit -ToUnit $optUnit
                $currentFmt = Format-Value -Value $convertedCurrent -Unit $optUnit

                if ($null -ne $baseVal -and $baseVal -ne 0) {
                    # Baseline calculation and ratio mapping
                    $ratio = $currentVal / $baseVal

                    # Optional: Check if we have Standard Deviation to calculate RatioSD
                    $parentPath = if ($col.Key.LastIndexOf('.') -gt 0) { $col.Key.Substring(0, $col.Key.LastIndexOf('.')) } else { $null }
                    $currentSd = if ($parentPath) { Get-NestedProperty -obj $bench -path "$parentPath.StandardDeviation" } else { $null }
                    $baseSd = if ($parentPath) { Get-NestedProperty -obj $baseline -path "$parentPath.StandardDeviation" } else { $null }

                    # Build Ratio display string with propagation of uncertainty if SD is available
                    if ($null -ne $currentSd -and $null -ne $baseSd -and $currentVal -ne 0) {
                        $ratioSd = $ratio * [math]::Sqrt([math]::Pow($currentSd / $currentVal, 2) + [math]::Pow($baseSd / $baseVal, 2))
                        $ratioStr = "{0:N2} ± {1:N2}" -f $ratio, $ratioSd
                    } else {
                        $ratioStr = "{0:N2}" -f $ratio
                    }

                    $cellText = "$currentFmt ($ratioStr)"

                    # Evaluate Threshold limits (Regressions)
                    if ($col.Threshold -gt 1 -and $ratio -gt $col.Threshold) {
                        $isFailed = $true
                    } elseif ($col.Threshold -lt 1 -and $ratio -lt $col.Threshold) {
                        $isFailed = $true
                    }

                    # Evaluate Inverted Threshold limits (Improvements)
                    if ($col.Threshold -gt 1 -and $ratio -lt (1 / $col.Threshold)) {
                        $isImproved = $true
                    } elseif ($col.Threshold -lt 1 -and $ratio -gt (1 / $col.Threshold)) {
                        $isImproved = $true
                    }

                } else {
                    $cellText = $currentFmt
                }
            }

            # Status indicators
            $star      = [char]::ConvertFromUtf32(0x2B50)       # ⭐ Best value
            $improved  = [char]::ConvertFromUtf32(0x1F7E2)      # 🟢 Performance improvement
            $regressed = [char]::ConvertFromUtf32(0x1F534)      # 🔴 Performance regression

            if ($isFailed) {
                $overallFailure = $true
                $hasRegressions = $true
                $cellText = "**$cellText** $regressed"
            } elseif ($isImproved) {
                $hasImprovements = $true
                $cellText = "**$cellText** $improved"
            }

            # Best value star logic
            $isBest = ($bestValues.Contains($col.Key) -and $currentVal -eq $bestValues[$col.Key])
            if ($isBest) {
                $cellText = "$cellText $star"
            }

            $rowCells.Add($cellText)
        }

        # Write row to Markdown
        $md.AppendLine("| $(($rowCells -join ' | ')) |") | Out-Null
    }

    # Update bench group status indicator
    $regressionIndicator = if ($hasRegressions) {":red_circle:"} else { "" }
    $md.Replace("{{BENCH_HASREGRESSIONS}}", $regressionIndicator ) | Out-Null

    $improvementIndicator = if ($hasImprovements) {":green_circle:"} else { "" }
    $md.Replace("{{BENCH_HASIMPROVEMENTS}}", $improvementIndicator ) | Out-Null

    $md.AppendLine("</details>") | Out-Null
    $md.AppendLine() | Out-Null
}

# Ensure destination directory exists before writing
$outDir = Split-Path $ComparisonResult -Parent
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

# Update overall status indicator
$statusEmoji = if ($overallFailure) { ":no_entry_sign:" } else { ":thumbsup:" }
$md.Replace("{{STATUS_EMOJI}}", $statusEmoji) | Out-Null

# Close top level collapsible details
$md.AppendLine("</details>") | Out-Null

# Output to Markdown file
$md.ToString() | Set-Content -Path $ComparisonResult -Encoding UTF8

# --- NATIVE GH CLI COMMENTING LOGIC ---
if ($env:GITHUB_REF -match "refs/pull/(\d+)/merge") {
    $prNumber =$matches[1]

    # 1. Fetch ALL comments across all pages and parse into PowerShell objects
    $commentsJson = gh api "repos/$env:GITHUB_REPOSITORY/issues/$prNumber/comments" --paginate | ConvertFrom-Json

    # 2. Filter for any comment containing your tag
    $matchingComments =$commentsJson | Where-Object { $_.body -match "tag:$CommentTag" }

    # 3. Delete matching previous comments
    foreach ($comment in $matchingComments) {
        Write-Host "Deleting previous benchmark comment ID: $($comment.id)"
        gh api --method DELETE "repos/$env:GITHUB_REPOSITORY/issues/comments/$($comment.id)" | Out-Null
    }

    # 4. Post the new comment at the end of the PR
    Write-Host "Posting new benchmark comment to PR #$prNumber..."
    gh pr comment $prNumber --body-file $ComparisonResult | Out-Null
} else {
    Write-Host "Not running in a Pull Request context. Skipping comment posting."
}

# --- EVALUATE REGRESSION FAIL SWITCH ---
if ($overallFailure) {
    Write-Warning "Performance regression detected! One or more benchmarks exceeded their configured thresholds."

    if ($FailOnRegression) {
        Write-Error "Failing the workflow step because -FailOnRegression is set to true."
        exit 1
    } else {
        Write-Host "Regressions found, but -FailOnRegression is false. Exiting gracefully without failing build."
    }
}