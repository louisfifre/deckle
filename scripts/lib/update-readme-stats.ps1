# update-readme-stats.ps1
#
# Regenerates the small "Development pulse" section in README.md from the
# local Git history. The section is bounded by invisible HTML comments so the
# visible README stays clean while automation has a stable replacement target.

[CmdletBinding()]
param(
    # Repository/worktree root. Defaults to the current Git worktree.
    [string]$Target,

    # Interactive picker: lists the main repo + all linked worktrees and
    # prompts for a choice. Overrides -Target.
    [switch]$Pick,

    # Override the README path. Mostly useful for tests.
    [string]$ReadmePath,

    # Commit the generated root README locally when it changes. Never pushes.
    [switch]$Commit
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')

function Step($msg) { Write-Host "`n[readme] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "         $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "         $msg" -ForegroundColor Yellow }

$Workflow = 'Update README pulse'
$RepoRoot = $null
$CommitCreated = $false
$CommitSubject = 'docs(readme): refresh development pulse'

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "README development pulse update failed before completion." `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            README   = $ReadmePath
            Committed = $CommitCreated
            Error    = $_.Exception.Message
        })
    throw
}

if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Resolve-Path -LiteralPath $Target).Path
} else {
    $RepoRoot = (git rev-parse --show-toplevel).Trim()
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

if (-not $ReadmePath) {
    $ReadmePath = Join-Path $RepoRoot 'README.md'
}
Write-Host "README: $ReadmePath" -ForegroundColor DarkGray

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "README.md not found: $ReadmePath"
}

$StartMarker = '<!-- deckle-stats:start -->'
$EndMarker   = '<!-- deckle-stats:end -->'
$PulsePattern = "(?s)$([regex]::Escape($StartMarker)).*?$([regex]::Escape($EndMarker))"
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

function Get-ReadmeWithoutPulse {
    param([Parameter(Mandatory)][string]$Content)

    $normalized = $Content -replace "\r?\n", "`n"
    return ([regex]::Replace($normalized, $PulsePattern, '')).TrimEnd([char]10)
}

if ($Commit) {
    $rootReadmePath = [IO.Path]::GetFullPath((Join-Path $RepoRoot 'README.md'))
    $resolvedReadmePath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ReadmePath).Path)
    if (-not $resolvedReadmePath.Equals($rootReadmePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'README commit is available only for the root README.md. Run without -Commit for another output path.'
    }

    $stagedPaths = @(Invoke-Git diff --cached --name-only)
    if ($stagedPaths.Count -gt 0) {
        throw 'README commit stopped because staged changes already exist. Commit or unstage them, then run again.'
    }

    $otherTrackedPaths = @(Invoke-Git diff --name-only | Where-Object { $_ -cne 'README.md' })
    if ($otherTrackedPaths.Count -gt 0) {
        throw "README commit stopped because tracked changes exist outside README.md: $($otherTrackedPaths -join ', '). Commit or revert them, then run again."
    }

    $readmeDirty = @(Invoke-Git diff --name-only -- README.md).Count -gt 0
    if ($readmeDirty) {
        $headReadme = @(Invoke-Git show 'HEAD:README.md') -join "`n"
        $workingReadme = Get-Content -LiteralPath $ReadmePath -Raw
        if ((Get-ReadmeWithoutPulse -Content $workingReadme) -cne (Get-ReadmeWithoutPulse -Content $headReadme)) {
            throw 'README commit stopped because README.md has changes outside the generated development pulse. Commit or revert them, then run again.'
        }
    }
}

Step 'Collect Git history'
$commitCount = [int64](Invoke-Git rev-list --count HEAD)
$dates       = @(Invoke-Git log --date=short --format=%ad HEAD)
$firstDate   = if ($dates.Count -gt 0) { $dates[-1] } else { 'n/a' }
$activeDays  = [int64](@($dates | Sort-Object -Unique).Count)
Ok ("{0} commits across {1} active day(s)" -f (Format-Count $commitCount), (Format-Count $activeDays))

Step 'Measure churn'
$additions = [int64]0
$deletions = [int64]0
foreach ($line in (Invoke-Git log --numstat --pretty=tformat: HEAD)) {
    if ($line -match '^(\d+)\s+(\d+)\s+.+$') {
        $additions += [int64]$matches[1]
        $deletions += [int64]$matches[2]
    }
}
$touched = $additions + $deletions
Ok ("{0} added / {1} touched lines" -f (Format-Count $additions), (Format-Count $touched))

Step 'Measure tracked text files'
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
Ok ("{0} tracked text files / {1} current lines" -f (Format-Count $trackedTextFiles), (Format-Count $trackedTextLines))

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

Step 'Render README development pulse'
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
if ([regex]::IsMatch($readme, $PulsePattern)) {
    $updated = [regex]::Replace($readme, $PulsePattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $section })
    Ok "Existing generated section found"
} else {
    $dividerPattern = "(?m)^---\r?\n"
    $dividerMatch = [regex]::Match($readme, $dividerPattern)
    if (-not $dividerMatch.Success) {
        throw "Could not find README insertion point. Add $StartMarker / $EndMarker manually."
    }
    $updated = $readme.Insert($dividerMatch.Index, "$section`n`n")
    Warn "Generated section was missing; inserted before the first README divider"
}

$normalizedReadme = $readme -replace "\r?\n", "`n"
$normalizedUpdated = $updated -replace "\r?\n", "`n"

if ($normalizedUpdated -ne $normalizedReadme) {
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $ReadmePath).Path, $normalizedUpdated, $utf8NoBom)
    Write-StatsSummary
    Step 'Done'
    Ok "README.md development pulse updated"
    $readmeChanged = $true
} else {
    Write-StatsSummary
    Step 'Done'
    Ok "README.md development pulse already up to date"
    $readmeChanged = $false
}

if ($Commit) {
    $readmePending = @(Invoke-Git diff --name-only -- README.md).Count -gt 0
    if ($readmePending) {
        Step 'Commit README development pulse'
        Invoke-Git add -- README.md | Out-Null
        Invoke-Git commit -m $CommitSubject | Out-Null
        $CommitCreated = $true
        Ok $CommitSubject
    } else {
        Step 'Commit README development pulse'
        Ok 'No commit needed; README.md already matches HEAD'
    }
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $(if ($CommitCreated) { "README development pulse was regenerated and committed." } elseif ($readmeChanged) { "README development pulse was regenerated and written." } else { "README development pulse was already up to date." }) `
    -Details ([ordered]@{
        Worktree              = $RepoRoot
        README                = $ReadmePath
        Changed               = $(if ($readmeChanged) { 'Yes' } else { 'No' })
        Committed             = $(if ($Commit) { $(if ($CommitCreated) { 'Yes' } else { 'No change' }) } else { 'Not requested' })
        Commit                = $(if ($CommitCreated) { $CommitSubject } else { $null })
        Commits               = (Format-Count $commitCount)
        'Active days'         = (Format-Count $activeDays)
        'Tracked text files'  = (Format-Count $trackedTextFiles)
        'Current text lines'  = (Format-Count $trackedTextLines)
        'Generated on'        = $generatedDate
    })
