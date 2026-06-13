# update-readme-stats.ps1
#
# Regenerates the small "Development pulse" section in README.md from the
# local Git history. The section is bounded by invisible HTML comments so the
# visible README stays clean while automation has a stable replacement target.

[CmdletBinding()]
param(
    # Repository/worktree root. Defaults to the current Git worktree.
    [string]$Target,

    # Override the README path. Mostly useful for tests.
    [string]$ReadmePath
)

$ErrorActionPreference = 'Stop'

if ($Target) {
    $RepoRoot = (Resolve-Path -LiteralPath $Target).Path
} else {
    $RepoRoot = (git rev-parse --show-toplevel).Trim()
}

if (-not $ReadmePath) {
    $ReadmePath = Join-Path $RepoRoot 'README.md'
}

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "README.md not found: $ReadmePath"
}

$StartMarker = '<!-- deckle-stats:start -->'
$EndMarker   = '<!-- deckle-stats:end -->'
$Culture     = [System.Globalization.CultureInfo]::InvariantCulture

function Format-Count {
    param([Int64]$Value)
    return $Value.ToString('N0', $Culture)
}

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Args)
    $output = & git -C $RepoRoot @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Args -join ' ') failed with exit code $LASTEXITCODE"
    }
    return $output
}

$commitCount = [int64](Invoke-Git rev-list --count HEAD)
$dates       = @(Invoke-Git log --date=short --format=%ad HEAD)
$firstDate   = if ($dates.Count -gt 0) { $dates[-1] } else { 'n/a' }
$activeDays  = [int64](@($dates | Sort-Object -Unique).Count)

$additions = [int64]0
$deletions = [int64]0
foreach ($line in (Invoke-Git log --numstat --pretty=tformat: HEAD)) {
    if ($line -match '^(\d+)\s+(\d+)\s+.+$') {
        $additions += [int64]$matches[1]
        $deletions += [int64]$matches[2]
    }
}
$touched = $additions + $deletions

$trackedTextLines = [int64]0
$trackedTextFiles = [int64]0
foreach ($path in (Invoke-Git ls-files)) {
    $absolute = Join-Path $RepoRoot $path
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }

    $bytes = [System.IO.File]::ReadAllBytes($absolute)
    if ($bytes.Length -gt 0 -and [Array]::IndexOf($bytes, [byte]0) -ge 0) {
        continue
    }

    $lines = [int64]0
    foreach ($byte in $bytes) {
        if ($byte -eq 10) { $lines++ }
    }
    if ($bytes.Length -gt 0 -and $bytes[$bytes.Length - 1] -ne 10) {
        $lines++
    }

    $trackedTextLines += $lines
    $trackedTextFiles++
}

$generatedDate = (Get-Date).ToString('yyyy-MM-dd', $Culture)

function Write-StatsSummary {
    Write-Host ""
    Write-Host "Development pulse" -ForegroundColor Cyan
    Write-Host ("  First commit          : {0}" -f $firstDate)
    Write-Host ("  Commits               : {0}" -f (Format-Count $commitCount))
    Write-Host ("  Active days           : {0}" -f (Format-Count $activeDays))
    Write-Host ("  Lines added           : {0}" -f (Format-Count $additions))
    Write-Host ("  Lines touched         : {0}" -f (Format-Count $touched))
    Write-Host ("  Current tracked lines : {0}" -f (Format-Count $trackedTextLines))
    Write-Host ("  Tracked text files    : {0}" -f (Format-Count $trackedTextFiles))
    Write-Host ("  Generated on          : {0}" -f $generatedDate)
    Write-Host ""
}

$section = @"
$StartMarker
## Development pulse

| First commit | Commits | Active days | Lines added | Lines touched | Current tracked lines |
|---:|---:|---:|---:|---:|---:|
| $firstDate | $(Format-Count $commitCount) | $(Format-Count $activeDays) | $(Format-Count $additions) | $(Format-Count $touched) | $(Format-Count $trackedTextLines) |

<sub>Generated from Git history on $generatedDate. Counts include tracked text files only for the current line total.</sub>
$EndMarker
"@

$readme = Get-Content -LiteralPath $ReadmePath -Raw
$pattern = "(?s)$([regex]::Escape($StartMarker)).*?$([regex]::Escape($EndMarker))"
if ([regex]::IsMatch($readme, $pattern)) {
    $updated = [regex]::Replace($readme, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $section })
} else {
    $dividerPattern = "(?m)^---\r?\n"
    $dividerMatch = [regex]::Match($readme, $dividerPattern)
    if (-not $dividerMatch.Success) {
        throw "Could not find README insertion point. Add $StartMarker / $EndMarker manually."
    }
    $updated = $readme.Insert($dividerMatch.Index, "$section`n`n")
}

$normalizedReadme = $readme -replace "\r?\n", "`n"
$normalizedUpdated = $updated -replace "\r?\n", "`n"

if ($normalizedUpdated -ne $normalizedReadme) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $ReadmePath).Path, $normalizedUpdated, $utf8NoBom)
    Write-StatsSummary
    Write-Host "README.md development pulse updated."
} else {
    Write-StatsSummary
    Write-Host "README.md development pulse already up to date."
}
