# record-version.ps1 - Cut or finish a version without creating a GitHub Release.
#
# This is the frequent versioning path: create the vX.Y.Z commit/tag when needed,
# regenerate CHANGELOG.md from git history, commit that generated section, and
# optionally push the branch + tag. It deliberately stops before dotnet publish
# and gh release create, so a version can be traceable without becoming a public
# downloadable release.
#
# Idempotence:
#   - If the current <Version> already has a tag but CHANGELOG.md lacks its
#     section, the script finishes that already-cut version.
#   - Otherwise it bumps the chosen SemVer segment via cut-version.ps1.
#   - Re-running after the changelog was already baked leaves no changelog commit.

[CmdletBinding(DefaultParameterSetName = 'Next')]
param(
    # SemVer segment to increment when cutting the next version.
    [Parameter(ParameterSetName = 'Next')]
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    # Finish the current <Version> instead of bumping. The vX.Y.Z tag must exist.
    [Parameter(ParameterSetName = 'Current')]
    [switch]$Current,

    # Target worktree. Resolves like the other lib scripts: -Pick prompts for
    # one, -Target takes an explicit path, otherwise the repo two levels up.
    [string]$Target,
    [switch]$Pick,

    # Also push the current branch and the version tag. This publishes git
    # history only; it does not create a GitHub Release or upload artifacts.
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')

function Step($msg) { Write-Host "`n[record-version] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "                 $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "                 $msg" -ForegroundColor Yellow }

$Workflow = 'Record version'
$RepoRoot = $null
$Version = $null
$Tag = $null
$Mode = $null
$ChangelogCommit = $null
$Pushed = $false

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Version recording failed before completion." `
        -Details ([ordered]@{
            Worktree          = $RepoRoot
            Mode              = $Mode
            Version           = $(if ($Tag) { $Tag } elseif ($Version) { "v$Version" } else { $null })
            'Changelog commit' = $ChangelogCommit
            Pushed            = $(if ($Pushed) { 'Yes' } else { 'No' })
            Error             = $_.Exception.Message
        })
    throw
}

function Get-AppVersion([string]$Root) {
    $csproj = Join-Path $Root 'src\Deckle.App\Deckle.App.csproj'
    if (-not (Test-Path $csproj)) { throw "csproj not found at $csproj - is '$Root' a Deckle worktree?" }
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if (-not $m) { throw "<Version> not found in $csproj" }
    $v = $m.Matches[0].Groups[1].Value.Trim()
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "Current <Version> '$v' is not MAJOR.MINOR.PATCH - refuse to guess." }
    return $v
}

function Test-VersionTag([string]$Root, [string]$VersionText) {
    $existing = & git -C $Root tag --list "v$VersionText"
    if ($LASTEXITCODE -ne 0) { throw "git tag --list failed (code $LASTEXITCODE)" }
    return [bool]$existing
}

function Test-ChangelogSection([string]$Root, [string]$VersionText) {
    $path = Join-Path $Root 'CHANGELOG.md'
    if (-not (Test-Path $path)) { return $false }
    $content = Get-Content -Raw -LiteralPath $path
    $escaped = [regex]::Escape($VersionText)
    return $content -match "(?m)^## \[$escaped\]"
}

function Get-RecordableCommitSubjects([string]$Root, [string]$Range) {
    $subjects = & git -C $Root log --format='%s' $Range
    if ($LASTEXITCODE -ne 0) { throw "git log $Range failed (code $LASTEXITCODE)" }
    return , @($subjects | Where-Object {
        $_ -cmatch '^(feat|fix|perf|refactor|revert)(?:\([^)]+\))?!?:\s+'
    })
}

function Assert-NoTrackedChanges([string]$Root, [bool]$AllowChangelog) {
    $dirty = & git -C $Root status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0) { throw "git status failed (code $LASTEXITCODE)" }
    $other = @($dirty | Where-Object {
        $line = $_
        if (-not $line) { $false } else {
            $path = if ($line.Length -gt 3) { $line.Substring(3) } else { $line }
            (-not $AllowChangelog) -or $path -ne 'CHANGELOG.md'
        }
    })
    if ($other.Count) {
        $subject = if ($AllowChangelog) { 'Tracked changes other than CHANGELOG.md are pending' } else { 'Tracked changes are pending' }
        throw "$subject - commit or stash them first:`n$($other -join "`n")"
    }
}

# -- Resolve target worktree --------------------------------------------------
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}
Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

# -- Decide whether to finish the current cut or cut the next version ----------
$Version = Get-AppVersion $RepoRoot
$Tag = "v$Version"
$currentTagExists = Test-VersionTag $RepoRoot $Version
$currentSectionExists = Test-ChangelogSection $RepoRoot $Version
$recordableSinceCurrent = if ($currentTagExists) { Get-RecordableCommitSubjects $RepoRoot "$Tag..HEAD" } else { @() }
$willRecordCurrent = $Current -or ($currentTagExists -and -not $currentSectionExists -and $recordableSinceCurrent.Count -eq 0)

Assert-NoTrackedChanges $RepoRoot -AllowChangelog:$willRecordCurrent

if ($Current) {
    if (-not $currentTagExists) { throw "Current version $Tag is not tagged; cannot record it with -Current." }
    if ($recordableSinceCurrent.Count -gt 0) {
        throw "$($recordableSinceCurrent.Count) user-facing commit(s) exist after $Tag - record a new version instead of finishing the current tag."
    }
    $Mode = 'Current'
    Step "Record existing $Tag"
} elseif ($currentTagExists -and -not $currentSectionExists) {
    if ($recordableSinceCurrent.Count -eq 0) {
        $Mode = 'Current'
        Step "Finish already-cut $Tag"
    } else {
        $Mode = "Bump $Bump"
        Step "$($recordableSinceCurrent.Count) user-facing commit(s) after $Tag - cut next version ($Bump)"
        & (Join-Path $ScriptDir 'cut-version.ps1') -Target $RepoRoot -Bump $Bump
        if (-not $?) { throw "cut-version.ps1 failed" }

        $Version = Get-AppVersion $RepoRoot
        $Tag = "v$Version"
    }
} else {
    $Mode = "Bump $Bump"
    Step "Cut next version ($Bump)"
    & (Join-Path $ScriptDir 'cut-version.ps1') -Target $RepoRoot -Bump $Bump
    if (-not $?) { throw "cut-version.ps1 failed" }

    $Version = Get-AppVersion $RepoRoot
    $Tag = "v$Version"
}

# -- Bake generated changelog section -----------------------------------------
Step "Regenerate CHANGELOG.md for $Tag"
& (Join-Path $ScriptDir 'changelog.ps1') -Target $RepoRoot
if (-not $?) { throw "changelog.ps1 failed" }

& git -C $RepoRoot add -- CHANGELOG.md
if ($LASTEXITCODE -ne 0) { throw "git add CHANGELOG.md failed (code $LASTEXITCODE)" }

& git -C $RepoRoot diff --cached --quiet -- CHANGELOG.md
$diffCode = $LASTEXITCODE
if ($diffCode -eq 1) {
    & git -C $RepoRoot commit -m "docs(changelog): bake the $Tag section"
    if ($LASTEXITCODE -ne 0) { throw "changelog bake commit failed (code $LASTEXITCODE)" }
    $ChangelogCommit = "docs(changelog): bake the $Tag section"
    Ok $ChangelogCommit
} elseif ($diffCode -eq 0) {
    $ChangelogCommit = 'Unchanged'
    Ok 'CHANGELOG.md already matched git history'
} else {
    throw "git diff --cached failed (code $diffCode)"
}

# -- Optional push: git history only, not a GitHub Release ---------------------
if ($Push) {
    Step "Push branch and $Tag"
    & git -C $RepoRoot push
    if ($LASTEXITCODE -ne 0) { throw "git push failed (code $LASTEXITCODE)" }
    & git -C $RepoRoot push origin $Tag
    if ($LASTEXITCODE -ne 0) { throw "git push origin $Tag failed (code $LASTEXITCODE)" }
    $Pushed = $true
    Ok "Pushed current branch and $Tag"
}

$sentence = if ($Pushed) {
    "Deckle $Tag was recorded in git history and pushed without creating a GitHub Release."
} else {
    "Deckle $Tag was recorded locally without creating a GitHub Release."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Worktree           = $RepoRoot
        Version            = $Tag
        Mode               = $Mode
        'Changelog commit' = $ChangelogCommit
        Pushed             = $(if ($Pushed) { 'Yes' } else { 'No' })
    }) `
    -Next $(if ($Pushed) {
        @("Publish a GitHub Release later with the Release menu when this version should become downloadable.")
    } else {
        @(
            "git -C `"$RepoRoot`" push"
            "git -C `"$RepoRoot`" push origin $Tag"
            "Publish a GitHub Release later with the Release menu when this version should become downloadable."
        )
    })
