[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BenchmarkPath,
    [Parameter(Mandatory=$true)][string]$BaselinePath,
    [Parameter(Mandatory=$true)][string]$ComparisonResult,
    [Parameter(Mandatory=$true)][bool]$FailOnRegression,
    [Parameter(Mandatory=$true)][string]$CommentTag,
    [Parameter(Mandatory=$true)][string[]]$Display,
    [Parameter(Mandatory=$false)][string[]]$Compare
)

# 0. Imports
Import-Module "$PSScriptRoot/Conversion/Convert-BenchmarkValue.psm1" -Force
Import-Module "$PSScriptRoot/Formatting/Format-BenchmarkValue.psm1" -Force
Import-Module "$PSScriptRoot/Get-BenchmarkGroupName.psm1" -Force
Import-Module "$PSScriptRoot/Integration/Github/Render-GithubMarkdown.psm1" -Force
Import-Module "$PSScriptRoot/Integration/Github/Publish-GithubComment.psm1" -Force

# --- 1. Load Files ---
$benchFiles = Get-ChildItem -Path $BenchmarkPath -Filter "*-report-full-augmented.json"
if ($benchFiles.Count -eq 0) { throw "No benchmark JSON files found in '$BenchmarkPath'." }

$baseFiles = Get-ChildItem -Path $BaselinePath -Filter "*-report-full-augmented.json"
$isComparingAgainstSelf = $false
if ($baseFiles.Count -eq 0) {
    Write-Warning "No baseline JSON files found. Comparing against self."
    $baseFiles = $benchFiles
    $isComparingAgainstSelf = $true
}

$allBenchObjects = $benchFiles | ForEach-Object { Get-Content -Path $_.FullName | ConvertFrom-Json }
$allBaseObjects  = $baseFiles  | ForEach-Object { Get-Content -Path $_.FullName | ConvertFrom-Json }
$allBenchmarks     = $allBenchObjects | Select-Object -ExpandProperty Benchmarks
$allBaseBenchmarks = $allBaseObjects  | Select-Object -ExpandProperty Benchmarks

# --- 2. Helpers ---
$displayCols = foreach ($d in $Display) {
    $dict = @{}; $d -split '&' | ForEach-Object { $kv = $_ -split '='; $dict[$kv[0].ToLower()] = $kv[1] }
    [PSCustomObject]@{ Key=$dict['key']; Name=$dict['name']; Unit=$dict['unit']; Threshold=[double]$dict['threshold'] }
}

$compareCols = foreach ($c in $Compare) {
    if ([string]::IsNullOrWhiteSpace($c)) { continue }
    $dict = @{}; $c -split '&' | ForEach-Object { $kv = $_ -split '='; $dict[$kv[0].ToLower()] = $kv[1] }
    [PSCustomObject]@{ Key=$dict['key']; Name=$dict['name']; BiggerIsBetter=[System.Convert]::ToBoolean($dict['biggerisbetter']) }
}

function Get-NestedProperty {
    param($obj, [string]$path)
    $current = $obj
    foreach ($part in $path.Split('.')) {
        if ($null -eq $current) { return $null }
        $current = $current.$part
    }
    return $current
}

function Get-ParsedParameters {
    param([string]$ParamString)
    $dict = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($ParamString) -and $ParamString -ne "None") {
        $parts = $ParamString -split '&'
        foreach ($part in $parts) {
            $kv = $part -split '=', 2
            if ($kv.Count -eq 2) {
                $dict[[uri]::UnescapeDataString($kv[0].Replace("+", " ").Trim())] = [uri]::UnescapeDataString($kv[1].Replace("+", " ").Trim())
            } else {
                $dict["Param_$($dict.Count)"] = [uri]::UnescapeDataString($part.Replace("+", " ").Trim())
            }
        }
    }
    return $dict
}

# --- 3. Build ViewModel ---
$overallFailure = $false

# Map Environment
$envProps = @("BenchmarkDotNetCaption", "BenchmarkDotNetVersion", "OsVersion", "ProcessorName", "RuntimeVersion", "Configuration")
$environment = foreach ($prop in $envProps) {
    $bVal = Get-NestedProperty -obj $allBenchObjects[0] -path "HostEnvironmentInfo.$prop"
    $baseVal = Get-NestedProperty -obj $allBaseObjects[0] -path "HostEnvironmentInfo.$prop"
    [PSCustomObject]@{ Label = $prop; Current = $bVal; Baseline = $baseVal; HasChanged = ($bVal -ne $baseVal) }
}

# Group Benchmarks
$groupedBenchmarks = $allBenchmarks | Group-Object GroupingKey
$sortedGroups = foreach ($group in $groupedBenchmarks) { [PSCustomObject]@{ GroupName = Get-BenchmarkGroupName -Group $group; Data = $group } }
$sortedGroups = $sortedGroups | Sort-Object GroupName

$groupViewModels = foreach ($groupWrapper in $sortedGroups) {
    $groupData = $groupWrapper.Data.Group | Sort-Object SortingKey
    $groupBaseline = $groupData | Where-Object { $_.IsBaseline } | Select-Object -First 1

    $groupHasRegressions = $false
    $groupHasImprovements = $false

    # Gather Params & Headers
    $allParamKeys = [System.Collections.Generic.List[string]]::new()
    $groupParamsMap = @{}
    foreach ($bench in $groupData) {
        $parsedParams = Get-ParsedParameters -ParamString ($bench.Parameters ?? "")
        $groupParamsMap[$bench.DisplayInfo] = $parsedParams
        foreach ($key in $parsedParams.Keys) { if ($key -notin $allParamKeys) { $allParamKeys.Add($key) } }
    }

    $headers = [System.Collections.Generic.List[string]]::new()
    $headers.Add("Method"); $headers.Add("JobId")
    foreach ($k in $allParamKeys) { $headers.Add($k) }
    foreach ($col in $displayCols) { $headers.Add($col.Name) }
    if ($null -ne $compareCols) { foreach ($col in $compareCols) { $headers.Add($col.Name) } }

    # Find Best Values & Units
    $bestValues = @{}; $optimalUnits = @{}
    foreach ($col in $displayCols) {
        $validValues = @($groupData | ForEach-Object { Get-NestedProperty -obj $_ -path $col.Key } | Where-Object { $null -ne $_ })
        if ($validValues.Count -gt 0) {
            $optimalUnits[$col.Key] = Get-BenchmarkDisplayUnit -Values ([double[]]$validValues) -Unit $col.Unit
            if ($groupData.Count -gt 1) {
                $bestValues[$col.Key] = if ($col.Threshold -gt 1) { ($validValues | Measure-Object -Minimum).Minimum } else { ($validValues | Measure-Object -Maximum).Maximum }
            }
        }
    }

    $bestRatios = @{}
    if ($null -ne $compareCols -and $null -ne $groupBaseline -and $groupData.Count -gt 1) {
        foreach ($col in $compareCols) {
            $baseVal = Get-NestedProperty -obj $groupBaseline -path $col.Key
            if ($null -ne $baseVal -and $baseVal -ne 0) {
                $validRatios = @($groupData | ForEach-Object {
                    $cVal = Get-NestedProperty -obj $_ -path $col.Key
                    if ($null -ne $cVal) { $cVal / $baseVal }
                })
                if ($validRatios.Count -gt 0) {
                    $bestRatios[$col.Key] = if ($col.BiggerIsBetter) { ($validRatios | Measure-Object -Maximum).Maximum } else { ($validRatios | Measure-Object -Minimum).Minimum }
                }
            }
        }
    }

    # Build Rows
    $rows = foreach ($bench in $groupData) {
        $baseline = $allBaseBenchmarks | Where-Object DisplayInfo -eq $bench.DisplayInfo | Select-Object -First 1
        $cells = [System.Collections.Generic.List[psobject]]::new()

        # Text Cells
        $cells.Add([PSCustomObject]@{ Text = ($bench.MethodTitle ?? "N/A").Replace('|', '-'); IsBest=$false; IsRegression=$false; IsImprovement=$false })
        $cells.Add([PSCustomObject]@{ Text = ($bench.JobId ?? "N/A").Replace('|', '-'); IsBest=$false; IsRegression=$false; IsImprovement=$false })

        $pDict = $groupParamsMap[$bench.DisplayInfo]
        foreach ($k in $allParamKeys) {
            $val = if ($pDict.Contains($k)) { $pDict[$k] } else { "N/A" }
            $cells.Add([PSCustomObject]@{ Text = $val.Replace('|', '-'); IsBest=$false; IsRegression=$false; IsImprovement=$false })
        }

        # Display Metric Cells
        foreach ($col in $displayCols) {
            $currentVal = Get-NestedProperty -obj $bench -path $col.Key
            $baseVal = Get-NestedProperty -obj $baseline -path $col.Key
            $cellText = "N/A"; $isFailed = $false; $isImproved = $false

            if ($null -ne $currentVal) {
                $optUnit = $optimalUnits[$col.Key]
                $convertedCurrent = Convert-BenchmarkValue -Value $currentVal -FromUnit $col.Unit -ToUnit $optUnit
                $currentFmt = Format-BenchmarkValue -Value $convertedCurrent -Unit $optUnit

                if ($null -ne $baseVal -and $baseVal -ne 0) {
                    $ratio = $currentVal / $baseVal
                    $parentPath = if ($col.Key.LastIndexOf('.') -gt 0) { $col.Key.Substring(0, $col.Key.LastIndexOf('.')) } else { $null }
                    $currentSd = if ($parentPath) { Get-NestedProperty -obj $bench -path "$parentPath.StandardDeviation" } else { $null }
                    $baseSd = if ($parentPath) { Get-NestedProperty -obj $baseline -path "$parentPath.StandardDeviation" } else { $null }

                    if ($null -ne $currentSd -and $null -ne $baseSd -and $currentVal -ne 0) {
                        $ratioSd = $ratio * [math]::Sqrt([math]::Pow($currentSd / $currentVal, 2) + [math]::Pow($baseSd / $baseVal, 2))
                        $ratioStr = "{0:N2} ± {1:N2}" -f $ratio, $ratioSd
                    } else {
                        $ratioStr = "{0:N2}" -f $ratio
                    }

                    $cellText = "$currentFmt ($ratioStr)"

                    if (($col.Threshold -gt 1 -and $ratio -gt $col.Threshold) -or ($col.Threshold -lt 1 -and $ratio -lt $col.Threshold)) {
                        $isFailed = $true; $overallFailure = $true; $groupHasRegressions = $true
                    } elseif (($col.Threshold -gt 1 -and $ratio -lt (1 / $col.Threshold)) -or ($col.Threshold -lt 1 -and $ratio -gt (1 / $col.Threshold))) {
                        $isImproved = $true; $groupHasImprovements = $true
                    }
                } else {
                    $cellText = $currentFmt
                }
            }

            $isBest = ($bestValues.Contains($col.Key) -and $currentVal -eq $bestValues[$col.Key])
            $cells.Add([PSCustomObject]@{ Text = $cellText; IsBest = $isBest; IsRegression = $isFailed; IsImprovement = $isImproved })
        }

        # Compare Metric Cells
        if ($null -ne $compareCols) {
            foreach ($col in $compareCols) {
                if ($null -ne $groupBaseline) {
                    $currentVal = Get-NestedProperty -obj $bench -path $col.Key
                    $baseVal = Get-NestedProperty -obj $groupBaseline -path $col.Key
                    if ($null -ne $currentVal -and $null -ne $baseVal -and $baseVal -ne 0) {
                        $ratio = $currentVal / $baseVal
                        $isBest = ($bestRatios.Contains($col.Key) -and $ratio -eq $bestRatios[$col.Key])
                        $cells.Add([PSCustomObject]@{ Text = "{0:N2}" -f $ratio; IsBest = $isBest; IsRegression = $false; IsImprovement = $false })
                    } else {
                        $cells.Add([PSCustomObject]@{ Text = "N/A"; IsBest=$false; IsRegression=$false; IsImprovement=$false })
                    }
                } else {
                    $cells.Add([PSCustomObject]@{ Text = "N/A"; IsBest=$false; IsRegression=$false; IsImprovement=$false })
                }
            }
        }
        [PSCustomObject]@{ Cells = $cells }
    }

    [PSCustomObject]@{
        GroupName = $groupWrapper.GroupName
        HasRegressions = $groupHasRegressions
        HasImprovements = $groupHasImprovements
        Headers = $headers
        Rows = $rows
    }
}

$viewModel = [PSCustomObject]@{
    OverallFailure = $overallFailure
    IsComparingAgainstSelf = $isComparingAgainstSelf
    Environment = $environment
    Groups = $groupViewModels
}


# --- 4. Render and Publish ---

# Call b) to render Markdown
$markdown = Render-GithubMarkdown -ViewModel $viewModel -CommentTag $CommentTag

# Call c) to publish the comment
Publish-GithubComment -Markdown $markdown -ComparisonResultPath $ComparisonResult -CommentTag $CommentTag

# Evaluate FailOnRegression
if ($viewModel.OverallFailure) {
    Write-Warning "Performance regression detected! One or more benchmarks exceeded their configured thresholds."

    if ($FailOnRegression) {
        Write-Error "Failing the workflow step because -FailOnRegression is set to true."
        exit 1
    } else {
        Write-Host "Regressions found, but -FailOnRegression is false. Exiting gracefully without failing build."
    }
}