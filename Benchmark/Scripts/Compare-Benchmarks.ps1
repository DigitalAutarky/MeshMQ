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

# Helper: Parse BenchmarkDotNet Parameter String into a Dictionary
function Get-ParsedParameters {
    param([string]$ParamString)
    $dict = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($ParamString) -and $ParamString -ne "None") {
        # BenchmarkDotNet 'fulljson' exporter encodes parameters like a URL query string (using '&')
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
    $md.AppendLine(">") | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.BenchmarkDotNetCaption" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.BenchmarkDotNetVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.OsVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.ProcessorName" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.RuntimeVersion" -bench $bench -base $base | Out-Null
    Render-ExecutionContext-Element -md $md -key "HostEnvironmentInfo.Configuration" -bench $bench -base $base | Out-Null
    $md.AppendLine(">") | Out-Null
    $md.AppendLine("> </div>") | Out-Null
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
        $result = "$\color{orange}{\mathbf{\text{$result (changed)}}}$"
        $result = "<abbr title=""$baseValue"">$result</abbr>"
    }

    
    $result = "> $result"
    $md.AppendLine($result) | Out-Null
}

# 3. Create a sorted list of benchmarks by FullName descending
$sortedBenchmarks = $benchJson.Benchmarks | Sort-Object FullName -Descending

$md = [System.Text.StringBuilder]::new()

# 4. Write title and comment anchor tag into Markdown file
$md.AppendLine("<!-- tag:$CommentTag -->") | Out-Null # <--- Anchor for sticky finding

$md.AppendLine("<details>") | Out-Null
$md.AppendLine("<summary>") | Out-Null
$md.AppendLine("") | Out-Null
$md.AppendLine("# Benchmark Summary {{STATUS_EMOJI}}") | Out-Null
$md.AppendLine("") | Out-Null
$md.AppendLine("</summary>") | Out-Null
$md.AppendLine("") | Out-Null
Render-ExecutionContext -md $md -bench $benchJson -base $baseJson | Out-Null
$md.AppendLine("") | Out-Null

$overallFailure = $false

# Group benchmarks by their Type AND MethodTitle
$groupedBenchmarks = $sortedBenchmarks | Group-Object Type, MethodTitle

foreach ($group in $groupedBenchmarks) {
    $firstItem = $group.Group[0]
    $groupType = $firstItem.Type
    $groupMethod = $firstItem.MethodTitle

    # 1. Write the Header format
    $md.AppendLine("### $groupType.$groupMethod") | Out-Null
    $md.AppendLine() | Out-Null

    # 2. Gather all unique parameters for this group to create dynamic columns
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

    # 3. Construct Standard Markdown Table Headers
    $headerCells = [System.Collections.Generic.List[string]]::new()
    $separatorCells = [System.Collections.Generic.List[string]]::new()

    # Add dynamic parameter headers (if any)
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
    $md.AppendLine("| $(($headerCells -join ' | ')) |") | Out-Null
    $md.AppendLine("| $(($separatorCells -join ' | ')) |") | Out-Null

    # 4. Iterate through the sorted list
    foreach ($bench in $group.Group) {
        $baseline = $baseJson.Benchmarks | Where-Object FullName -eq $bench.FullName | Select-Object -First 1

        $rowCells = [System.Collections.Generic.List[string]]::new()
        $pDict = $groupParamsMap[$bench.FullName]

        # Render dynamic parameter cell contents
        foreach ($k in $allParamKeys) {
            $val = if ($pDict.Contains($k)) { $pDict[$k] } else { "N/A" }
            $rowCells.Add($val.Replace('|', '-')) # Escape pipes for markdown tables
        }

        # Render statistical display cells
        foreach ($col in $displayCols) {
            $currentVal = Get-NestedProperty -obj $bench -path $col.Key
            $baseVal = Get-NestedProperty -obj $baseline -path $col.Key

            $cellText = "N/A"
            $isFailed = $false

            if ($null -ne $currentVal) {
                $currentFmt = "{0:N2}" -f $currentVal

                if ($null -ne $baseVal -and $baseVal -ne 0) {
                    $ratio = $currentVal / $baseVal
                    $ratioStr = "{0:N2}" -f $ratio
                    $cellText = "$currentFmt ($ratioStr)"

                    # Check Threshold limits
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
                $cellText = "$\color{red}{\mathbf{\text{$cellText}}}$"
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

# Update overall status indicator
$statusEmoji = if ($overallFailure) { ":no_entry_sign:" } else { ":thumbsup:" }
$md.Replace("{{STATUS_EMOJI}}", $statusEmoji) | Out-Null

#close top level collapsible details
$md.AppendLine("</details>") | Out-Null

# Output to Markdown file
$md.ToString() | Set-Content -Path $ComparisonResult -Encoding UTF8

# --- NATIVE GH CLI COMMENTING LOGIC ---
if ($env:GITHUB_REF -match "refs/pull/(\d+)/merge") {
    $prNumber =$matches[1]

    # 1. Fetch ALL comments across all pages and parse into PowerShell objects
    $commentsJson = gh api "repos/$env:GITHUB_REPOSITORY/issues/$prNumber/comments" --paginate | ConvertFrom-Json

    # 2. Filter for any comment containing your tag
    $matchingComments =$commentsJson | Where-Object { $_.body -like "*tag:$CommentTag*" }

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