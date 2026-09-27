# Load view model type for strict type safety
if (-not ('Benchmark.BenchmarkViewModel' -as [type])) {
    Add-Type -Path "$PSScriptRoot/../BenchmarkViewModel.cs"
}

# Global Variables
$star      = [char]::ConvertFromUtf32(0x2B50)
$improved  = [char]::ConvertFromUtf32(0x1F7E2)
$regressed = [char]::ConvertFromUtf32(0x1F534)

# Render a collapsible detais section
function Render-GithubDetailsSection
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][String]$Summary,
        [Parameter(Mandatory = $true)][AllowEmptyString()][String]$Content,
        [Parameter(Mandatory = $true)][Bool]$IsOpen
    )

    $md = [System.Text.StringBuilder]::new() 
    $md.AppendLine("<details$($IsOpen ? ' open' : '')>") | Out-Null
    $md.AppendLine("<summary>`n`n### $summary`n`n</summary>`n") | Out-Null
    $md.AppendLine($Content) | Out-Null
    $md.AppendLine("</details>`n") | Out-Null
    
    return $md.ToString()
}

# Render Github Markdown Table
function Render-GithubMarkdownTable
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group
    )

    $md = [System.Text.StringBuilder]::new()

    # Write Headers & Separators
    $md.AppendLine("| $($Group.Headers -join ' | ') |") | Out-Null
    $separators = $Group.Headers | ForEach-Object { ":---" }
    $md.AppendLine("| $($separators -join ' | ') |") | Out-Null

    # Write table rows
    foreach ($row in $Group.Rows) {
        $formattedCells = foreach ($cell in $row.Cells) {
            $txt = $cell.Text
            if ($cell.IsRegression) { $txt = "**$txt** $regressed" }
            elseif ($cell.IsImprovement) { $txt = "**$txt** $improved" }
            
            if ($cell.IsBest) { $txt = "$txt $star" }
            $txt
        }
        
        $md.AppendLine("| $($formattedCells -join ' | ') |") | Out-Null
    }
    
    return $md.ToString()
}

# Render a benchmark dotnet logical group
function Render-LogicalGroup
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group
    )

    # Define performance indicators
    $regInd = if ($group.HasRegressions) { $regressed } else { "" }
    $impInd = if ($group.HasImprovements) { $improved  } else { "" }

    # Render logical group as table in a detail section
    $summary = "### $($group.GroupName) $regInd$impInd"
    $groupTable = Render-GithubMarkdownTable -Group $group
    $groupDetail = Render-GithubDetailsSection -Summary $summary -Content $groupTable -IsOpen $group.HasRegressions

    return $groupDetail
}

# Render Github Markdown as string
function Render-GithubMarkdown {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][Benchmark.BenchmarkViewModel]$ViewModel,
        [Parameter(Mandatory=$true)][string]$CommentTag
    )

    $md = [System.Text.StringBuilder]::new()
    $md.AppendLine("<!-- tag:$CommentTag -->") | Out-Null

    if ($ViewModel.IsComparingAgainstSelf) {
        $md.AppendLine("> [!WARNING]") | Out-Null
        $md.AppendLine("> No baseline JSON found so this run compared the benchmark result against itself.") | Out-Null
    }

    $statusEmoji = if ($ViewModel.OverallFailure) { ":no_entry_sign:" } else { ":thumbsup:" }
    $md.AppendLine("<details><summary>`n`n## Benchmark Results $statusEmoji`n`n</summary>`n`n") | Out-Null

    # Render Environment
    $md.AppendLine("> <div align=""center"">`n> ") | Out-Null
    foreach ($env in $ViewModel.Environment) {
        $result =$env.Current
        if ($env.HasChanged) {$result = "$\color{orange}{\mathbf{\text{$result (was: $($env.Baseline))}}}$" }
        $md.AppendLine("> $result") | Out-Null
    }
    $md.AppendLine("> `n> </div>`n___`n`n") | Out-Null

    # Render Logical Groups and and collect them
    # in failed/succeeded string builders
    $totalCount = 0
    $failedCount = 0
    $secceededCount = 0
    $failed = [System.Text.StringBuilder]::new()
    $succeeded = [System.Text.StringBuilder]::new()
    foreach ($group in $ViewModel.Groups) {
        $totalCount++
        if($group.HasRegressions) {
            $renderedGroup = Render-LogicalGroup -Group $group
            $failed.Append($renderedGroup) | Out-Null
            $failedCount++
        }
        else {
            $renderedGroup = Render-LogicalGroup -Group $group
            $succeeded.Append($renderedGroup) | Out-Null
            $secceededCount++
        }
    }
    
    # Finally add our collected items to the main output
    $failures = Render-GithubDetailsSection -Summary "Failed ($failedCount/$totalCount)" -Content $failed -IsOpen $true
    $md.Append($failures) | Out-Null

    $successes = Render-GithubDetailsSection -Summary "Succeeded ($secceededCount/$totalCount)" -Content $succeeded -IsOpen $false
    $md.Append($successes) | Out-Null
    
    # Done
    $md.AppendLine("</details>") | Out-Null
    return $md.ToString()
}