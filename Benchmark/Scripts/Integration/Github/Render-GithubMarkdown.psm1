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

# Render manual tables in a code block so we can align verything
function Render-PaddedCodeBlockTable
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group,
        [Parameter(Mandatory = $true)][hashtable]$ColumnWidths
    )

    $md = [System.Text.StringBuilder]::new()
    $md.AppendLine("```text") | Out-Null

    # Write Headers
    $paddedHeaders = foreach ($header in $Group.Headers) {
        $header.PadRight($ColumnWidths[$header])
    }
    
    $md.AppendLine($paddedHeaders -join ' | ') | Out-Null

    # Write Separators
    $separators = foreach ($header in$Group.Headers) {
        "".PadRight($ColumnWidths[$header], '-')
    }
    
    $md.AppendLine($separators -join '-+-') | Out-Null

    # Write Rows
    foreach ($row in $Group.Rows) {
        $formattedCells = [System.Collections.Generic.List[string]]::new()

        for ($i = 0; $i -lt $Group.Headers.Count; $i++) {
            $cell =$row.Cells[$i]$header = $Group.Headers[$i]
            $txt =$cell.Text
    
            # Swap markdown/emojis for plain-text symbols
            if ($cell.IsRegression) { $txt = "$txt X" }
            elseif ($cell.IsImprovement) { $txt = "$txt ^" }
    
            if ($cell.IsBest) { $txt = "$txt *" }
    
            $formattedCells.Add($txt.PadRight($ColumnWidths[$header]))
    }

    $md.AppendLine($formattedCells -join ' | ') | Out-Null
    }

    $md.AppendLine("````") | Out-Null
    return $md.ToString()
}

# Render a benchmark dotnet logical group
function Render-LogicalGroup
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkGroup]$Group,
        [Parameter(Mandatory = $true)][int]$indentationLevel,
        [Parameter(Mandatory = $true)][bool]$IsOpen,
        [Parameter(Mandatory = $true)][hashtable]$ColumnWidths
    )

    # Render logical group as table in a detail section with anchor target
    $groupTable = Render-PaddedCodeBlockTable -Group$group -ColumnWidths $ColumnWidths
    $content = "<a name=""$($group.GroupKey)""></a>`n`n$groupTable"
    $groupDetail = Render-GithubDetailsSection -Summary $group.GroupName -Content $content -IsOpen $IsOpen
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
    $topChangesTable.AppendLine("<table>") | Out-Null
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

# Determins the required widths for columns
function Compute-TableWidths
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][Benchmark.BenchmarkViewModel]$ViewModel
    )

    # Pre-calculate global column widths across all groups for perfect alignment
    $colWidths = @{}
    foreach ($group in $ViewModel.Groups) {
        for ($i = 0; $i -lt $group.Headers.Count; $i++) {
            $header = $group.Headers[$i]
            $len = $header.Length
            if (-not $colWidths.Contains($header) -or $len -gt $colWidths[$header]) {
                $colWidths[$header] = $len
            }

            foreach ($row in $group.Rows) {
                # Account for text length plus plain-text markers (e.g., ' *', ' ^', ' X')
                $cellLen = $row.Cells[$i].Text.Length
                if ($row.Cells[$i].IsRegression -or $row.Cells[$i].IsImprovement -or $row.Cells[$i].IsBest) { $cellLen += 2 }

                if ($cellLen -gt $colWidths[$header]) {
                    $colWidths[$header] = $cellLen
                }
            }
        }
    }
    
    return $colWidths
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
    
    # Determine optimal columns widths for our table enderer
    $optimalColumnWidths = Compute-TableWidths -ViewModel $ViewModel
    
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
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $true -ColumnWidths $optimalColumnWidths
            $regressedResults.AppendLine($renderedGroup) | Out-Null
            $regressedCount++
        }
        elseif ($group.HasImprovements) {
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $true -ColumnWidths $optimalColumnWidths
            $improvedResults.AppendLine($renderedGroup) | Out-Null
            $improvedCount++
        }
        else {
            $renderedGroup = Render-LogicalGroup -Group $group -IndentationLevel 1 -IsOpen $false -ColumnWidths $optimalColumnWidths
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