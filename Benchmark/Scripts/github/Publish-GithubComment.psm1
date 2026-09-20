function Publish-GithubComment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$Markdown,
        [Parameter(Mandatory=$true)][string]$ComparisonResultPath,
        [Parameter(Mandatory=$true)][string]$CommentTag
    )

    # 1. Output to Markdown file
    $outDir = Split-Path $ComparisonResultPath -Parent
    if (-not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    $Markdown | Set-Content -Path $ComparisonResultPath -Encoding UTF8

    # 2. GitHub CLI Posting Logic
    if ($env:GITHUB_REF -match "refs/pull/(\d+)/merge") {
        $prNumber =$matches[1]

        Write-Host "Fetching previous benchmark comments..."
        $commentsJson = gh api "repos/$env:GITHUB_REPOSITORY/issues/$prNumber/comments" --paginate | ConvertFrom-Json
        $matchingComments = $commentsJson | Where-Object { $_.body -match "tag:$CommentTag" }

        foreach ($comment in $matchingComments) {
            Write-Host "Deleting previous benchmark comment ID: $($comment.id)"
            gh api --method DELETE "repos/$env:GITHUB_REPOSITORY/issues/comments/$($comment.id)" | Out-Null
        }

        Write-Host "Posting new benchmark comment to PR #$prNumber..."
        gh pr comment $prNumber --body-file $ComparisonResultPath | Out-Null
    } else {
        Write-Host "Not running in a Pull Request context. Skipping GitHub CLI comment posting."
    }
}