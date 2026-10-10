# Load view model type for strict type safety
if (-not ('Benchmark.BenchmarkViewModel' -as [type])) {
    Add-Type -Path "$PSScriptRoot/../BenchmarkViewModel.cs"
}

# Global Variables
$bestIcon      = [char]::ConvertFromUtf32(0x2B50)
$improvedIcon  = [char]::ConvertFromUtf32(0x1F7E2)
$regressedIcon = [char]::ConvertFromUtf32(0x1F534)

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
    $md.AppendLine("<summary><strong>$Summary</strong></summary>") | Out-Null
    $md.AppendLine() | Out-Null # CRITICAL: Blank line before markdown content
    $md.AppendLine($Content) | Out-Null
    $md.AppendLine() | Out-Null # CRITICAL: Blank line before closing tag
    $md.AppendLine("</details>") | Out-Null
    
    return $md.ToString()
}

function Render-GithubBlockquote
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][String]$Text,
        [Parameter(Mandatory = $true)][int]$IndentationLevel
    )

    # Create prefix based on the requested indentytion level
    $prefix = ""
    while($IndentationLevel-- -gt 0) {
        $prefix = "> $prefix"
    }
    
    # Splits on either standard newline or carriage return + newline
    $prefixed = ($Text -split '\r?\n' | ForEach-Object {
        "$prefix$_"
    }) -join "`n"
    
    return $prefixed
}

# Render Github Markdown Table
function Render-GithubMarkdownTable
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group
    )

    $md = [System.Text.StringBuilder]::new()

    # Write Headers
    $md.AppendLine("<table width=""100%"">") | Out-Null
    $md.AppendLine("<thead>") | Out-Null
    $md.AppendLine("<tr>") | Out-Null
    foreach ($header in $Group.Headers) {
        $md.AppendLine("<th>$header</th>") | Out-Null
    }
    $md.AppendLine("</tr>") | Out-Null
    $md.AppendLine("</thead>") | Out-Null

    # Write table rows
    $md.AppendLine("<tbody>") | Out-Null
    foreach ($row in $Group.Rows) {
        $md.AppendLine("<tr>") | Out-Null
        foreach ($cell in $row.Cells) {
            $txt = $cell.Text
            if ($cell.IsRegression) { $txt = "$txt $regressedIcon" }
            elseif ($cell.IsImprovement) { $txt = "$txt $improvedIcon" }
            
            if ($cell.IsBest) { $txt = "$txt $bestIcon" }
            $md.AppendLine("<td>$txt</td>") | Out-Null
        }
        $md.AppendLine("</tr>") | Out-Null
    }

    $md.AppendLine("</tbody>") | Out-Null
    $md.AppendLine("</table>") | Out-Null
    return $md.ToString()
}

# Render a benchmark dotnet logical group
function Render-LogicalGroup
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group,
        [Parameter(Mandatory = $true)][int]$indentationLevel,
        [Parameter(Mandatory = $true)][bool]$IsOpen
    )

    # Render logical group as table in a detail section with anchor target
    $groupTable = Render-GithubMarkdownTable -Group $Group
    $summaryWithAnchor = "<a id=""$($group.GroupKey)""></a>$($group.GroupName)"
    $groupDetail = Render-GithubDetailsSection -Summary $summaryWithAnchor -Content $groupTable -IsOpen $IsOpen
    $indented = Render-GithubBlockquote -Text $groupDetail -IndentationLevel $indentationLevel

    return $indented
}

# Render a benchmark dotnet logical group
function Render-LogicalGroupTopChanges
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup[]]$Groups,
        [Parameter(Mandatory = $true)][int]$IndentationLevel
    )

    # Render top changes table
    $topChangesTable = [System.Text.StringBuilder]::new()
    $topChangesTable.AppendLine("<table width=""100%"">") | Out-Null
    $topChangesTable.AppendLine("<thead>") | Out-Null
    $topChangesTable.AppendLine("<tr>") | Out-Null
    $topChangesTable.AppendLine("<th>Group</th>") | Out-Null
    $topChangesTable.AppendLine("<th>Method</th>") | Out-Null
    $topChangesTable.AppendLine("<th>Attribute</th>") | Out-Null
    $topChangesTable.AppendLine("<th>Ratio</th>") | Out-Null
    $topChangesTable.AppendLine("<th align=""center"">Status</th>") | Out-Null
    $topChangesTable.AppendLine("</tr>") | Out-Null
    $topChangesTable.AppendLine("</thead>") | Out-Null
    
    $hasSummaryItems = $false
    $topChangesTable.AppendLine("<tbody>") | Out-Null
    foreach ($group in $Groups) {
        $item = $group.TopChangeItem
        if ($null -ne $item -and ($item.IsRegression -or $item.IsImprovement)) {
            $hasSummaryItems = $true
            $icon = if ($item.IsRegression) { $regressedIcon } else { $improvedIcon }
            $groupLink = "<a href=""#$($group.GroupKey)"">$($group.GroupName)</a>"
            $topChangesTable.AppendLine("<tr>") | Out-Null
            $topChangesTable.AppendLine("<td>$groupLink</td>") | Out-Null
            $topChangesTable.AppendLine("<td>$($item.Method)</td>") | Out-Null
            $topChangesTable.AppendLine("<td>$($item.Attribute)</td>") | Out-Null
            $topChangesTable.AppendLine("<td>$($item.Ratio)</td>") | Out-Null
            $topChangesTable.AppendLine("<td align=""center"">$icon</td>") | Out-Null
            $topChangesTable.AppendLine("</tr>") | Out-Null
        }
    }
    $topChangesTable.AppendLine("</tbody>") | Out-Null
    $topChangesTable.AppendLine("</table>") | Out-Null
    
    # Return null if no changes were rendered
    if ($hasSummaryItems -eq $false) {
        return $null
    }
    
    # Wrap it in an open details section
    $topChangesDetail = Render-GithubDetailsSection -Summary "Top Changes" -Content $topChangesTable.ToString() -IsOpen $true
    
    # Indent with blockquote
    $indented = Render-GithubBlockquote -Text $topChangesDetail -IndentationLevel $IndentationLevel

    # Done
    return $indented
}

# Render Github Markdown as string
function Render-GithubMarkdown {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][Benchmark.BenchmarkViewModel]$ViewModel,
        [Parameter(Mandatory=$true)][string]$CommentTag
    )

    # Render tag which identifies our comments for updates/deletions
    $md = [System.Text.StringBuilder]::new()
    $md.AppendLine("<!-- tag:$CommentTag -->") | Out-Null

    # Render warning if no baseline was found
    if ($ViewModel.IsComparingAgainstSelf) {
        $md.AppendLine("> [!WARNING]") | Out-Null
        $md.AppendLine("> No baseline JSON found so this run compared the benchmark result against itself.") | Out-Null
    }

    # Begin Rendering main details sections
    $statusEmoji = if ($ViewModel.OverallFailure) { ":no_entry_sign:" } else { ":thumbsup:" }
    $md.AppendLine("<details><summary><strong>Benchmark Results $statusEmoji</strong></summary>") | Out-Null
    $md.AppendLine() | Out-Null
    
    # Render Environment
    $md.AppendLine("> <div align=""center"">`n> ") | Out-Null
    foreach ($env in $ViewModel.Environment) {
        $result =$env.Current
        if ($env.HasChanged) {$result = "<strong>$result (was: $($env.Baseline))</strong>" }
        $md.AppendLine("> $result") | Out-Null
    }
    $md.AppendLine("> `n> </div>`n___`n`n") | Out-Null

    # Render top changes summary if available
    $topChangesSummary = Render-LogicalGroupTopChanges -Groups $ViewModel.Groups -IndentationLevel 1
    if ($null -ne $topChangesSummary) {
        $topChangesDetail = Render-GithubDetailsSection -Summary "Quick Summary" -Content $topChangesSummary -IsOpen $true
        $md.AppendLine($topChangesDetail) | Out-Null
    }
    
    # Render Logical Groups into sections based on regressions/improvements
    $totalCount = 0
    $regressedCount = 0
    $improvedCount = 0
    $unchangedCount = 0
    $regressedResults = [System.Text.StringBuilder]::new()
    $improvedResults = [System.Text.StringBuilder]::new()
    $unchangedResults = [System.Text.StringBuilder]::new()
    foreach ($group in $ViewModel.Groups) {
        $totalCount++
        if($group.HasRegressions) {
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $true
            $regressedResults.AppendLine($renderedGroup) | Out-Null
            $regressedCount++
        }
        elseif ($group.HasImprovements) {
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $true
            $improvedResults.AppendLine($renderedGroup) | Out-Null
            $improvedCount++
        }
        else {
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $false
            $unchangedResults.AppendLine($renderedGroup) | Out-Null
            $unchangedCount++
        }
    }
    
    # Finally add our collected items to the main output
    if ($regressedCount -gt 0) {
        $failures = Render-GithubDetailsSection -Summary "Regressed ($regressedCount/$totalCount)" -Content $regressedResults -IsOpen $true
        $md.AppendLine($failures) | Out-Null    
    }

    if ($improvedCount -gt 0) {
        $successes = Render-GithubDetailsSection -Summary "Improved ($improvedCount/$totalCount)" -Content $improvedResults -IsOpen $true
        $md.AppendLine($successes) | Out-Null
    }

    if ($unchangedCount -gt 0) {
        $unchangedDetail = Render-GithubDetailsSection -Summary "Unchanged ($unchangedCount/$totalCount)" -Content $unchangedResults -IsOpen $false
        $md.AppendLine($unchangedDetail) | Out-Null
    }
    
    # Done
    $md.AppendLine("</details>") | Out-Null
    return $md.ToString()
}