# Load view model type for strict type safety
if (-not ('Benchmark.BenchmarkViewModel' -as [type])) {
    Add-Type -Path "$PSScriptRoot/../BenchmarkViewModel.cs"
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
    $md.AppendLine("<details><summary>`n`n## Benchmark Summary $statusEmoji`n`n</summary>`n`n") | Out-Null

    # Render Environment
    $md.AppendLine("> <div align=""center"">`n> ") | Out-Null
    foreach ($env in $ViewModel.Environment) {
        $result =$env.Current
        if ($env.HasChanged) {$result = "$\color{orange}{\mathbf{\text{$result (was: $($env.Baseline))}}}$" }
        $md.AppendLine("> $result") | Out-Null
    }
    $md.AppendLine("> `n> </div>`n___`n`n") | Out-Null

    # Render Groups
    $star      = [char]::ConvertFromUtf32(0x2B50)
    $improved  = [char]::ConvertFromUtf32(0x1F7E2)
    $regressed = [char]::ConvertFromUtf32(0x1F534)

    foreach ($group in $ViewModel.Groups) {
        $regInd = if ($group.HasRegressions) { ":red_circle:" } else { "" }
        $impInd = if ($group.HasImprovements) { ":green_circle:" } else { "" }

        $md.AppendLine("<details><summary>`n`n### $($group.GroupName) $regInd$impInd`n`n</summary>`n") | Out-Null

        # Headers & Separators
        $md.AppendLine("| $($group.Headers -join ' | ') |") | Out-Null
        $separators = $group.Headers | ForEach-Object { if ($_ -in @("Method", "JobId") -or $_ -match "^Param_") { ":---" } else { "---:" } }
        $md.AppendLine("| $($separators -join ' | ') |") | Out-Null

        # Rows
        foreach ($row in $group.Rows) {
            $formattedCells = foreach ($cell in $row.Cells) {
                $txt = $cell.Text
                if ($cell.IsRegression) { $txt = "**$txt** $regressed" }
                elseif ($cell.IsImprovement) { $txt = "**$txt** $improved" }

                if ($cell.IsBest) { $txt = "$txt $star" }
                $txt
            }
            $md.AppendLine("| $($formattedCells -join ' | ') |") | Out-Null
        }

        $md.AppendLine("</details>`n") | Out-Null
    }

    $md.AppendLine("</details>") | Out-Null
    return $md.ToString()
}