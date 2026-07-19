# record-release.ps1 - Finalize a successful public release locally.
#
# Adds the GitHub-created tag to release-history.json, freezes the accumulated
# [Unreleased] entries into its version section, commits both files, and may
# push the branch. It never builds artifacts or creates the GitHub Release.

[CmdletBinding()]
param(
    [string]$Target,
    [switch]$Pick,
    [string]$Version,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')
Import-Module (Join-Path $ScriptDir 'release-history.psm1') -Force

function Step($msg) { Write-Host "`n[record-release] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "                 $msg" -ForegroundColor Green }

$RepoRoot = $null
$Tag = $null
$Commit = $null
$Pushed = $false

trap {
    Write-DeckleActionSummary `
        -Workflow 'Record public release' `
        -Result Failed `
        -Sentence 'Public release recording failed before completion.' `
        -Details ([ordered]@{
            Worktree = $RepoRoot
            Version  = $Tag
            Commit   = $Commit
            Pushed   = $(if ($Pushed) { 'Yes' } else { 'No' })
            Error    = $_.Exception.Message
        })
    throw
}

if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

$csproj = Join-Path $RepoRoot 'src\Deckle.App\Deckle.App.csproj'
if (-not (Test-Path $csproj)) { throw "csproj not found at $csproj" }
$match = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $match) { throw "<Version> not found in $csproj" }
$current = $match.Matches[0].Groups[1].Value.Trim()
if (-not $Version) { $Version = $current }
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not MAJOR.MINOR.PATCH" }
if ($Version -ne $current) { throw "Release v$Version does not match current project version v$current" }
$Tag = "v$Version"

$dirty = & git -C $RepoRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0) { throw "git status failed (code $LASTEXITCODE)" }
if ($dirty) { throw "Tracked changes are pending - commit or stash them first:`n$($dirty -join "`n")" }

& git -C $RepoRoot rev-parse --verify --quiet "$Tag^{commit}" *> $null
if ($LASTEXITCODE -ne 0) { throw "Public release tag $Tag is missing locally" }

Step "Record public release $Tag"
$added = Add-PublishedReleaseTag -RepoRoot $RepoRoot -Tag $Tag
if ($added) { Ok 'release-history.json updated' } else { Ok 'Release already present in history' }

& (Join-Path $ScriptDir 'changelog.ps1') -Target $RepoRoot
if (-not $?) { throw 'changelog.ps1 failed' }

& git -C $RepoRoot add -- release-history.json CHANGELOG.md
if ($LASTEXITCODE -ne 0) { throw "git add failed (code $LASTEXITCODE)" }
& git -C $RepoRoot diff --cached --quiet -- release-history.json CHANGELOG.md
$diffCode = $LASTEXITCODE
if ($diffCode -eq 1) {
    $Commit = "docs(changelog): bake the $Tag section"
    & git -C $RepoRoot commit -m $Commit
    if ($LASTEXITCODE -ne 0) { throw "release record commit failed (code $LASTEXITCODE)" }
    Ok $Commit
} elseif ($diffCode -eq 0) {
    $Commit = 'Unchanged'
    Ok 'Release history and changelog already finalized'
} else {
    throw "git diff --cached failed (code $diffCode)"
}

if ($Push) {
    Step 'Push finalized release record'
    & git -C $RepoRoot push
    if ($LASTEXITCODE -ne 0) { throw "git push failed (code $LASTEXITCODE)" }
    $Pushed = $true
    Ok 'Branch pushed'
}

Write-DeckleActionSummary `
    -Workflow 'Record public release' `
    -Result Success `
    -Sentence "Public release $Tag was frozen into the changelog." `
    -Details ([ordered]@{
        Worktree = $RepoRoot
        Version  = $Tag
        Commit   = $Commit
        Pushed   = $(if ($Pushed) { 'Yes' } else { 'No' })
    })
