# clean.ps1 — Local workspace cleanup
#
# Removes generated build output from a Deckle worktree without touching
# anything tracked by git. All `.gitignore`d, rebuilt by the next build,
# safe to delete and re-create at will.
#
# Two locations, since the repo uses the .NET artifacts output layout
# (root Directory.Build.props sets ArtifactsPath):
#   - artifacts\{bin,obj,publish,package}\  — the consolidated output of
#     every src/ and tests/ project (they share the root layout).
#   - src\<m>\{bin,obj}\, tests\<m>\{bin,obj}\,
#     benchmark\asr\studies\<m>\{bin,obj}\
#     — stragglers: benchmark keeps the classic per-project layout (its
#     own Directory.Build.props opts out), and a worktree built before the
#     artifacts migration may still carry old per-module folders. Cleaned
#     too so a single run leaves the tree pristine.
#
# Released ZIP staging (artifacts\Deckle-v<X.Y.Z>\) is KEPT by default — it
# is a release artefact, not transient build output. Pass -IncludeReleases
# to purge it as well.
#
# The running Deckle process is stopped first: it locks Deckle.exe and its
# DLLs, and with $ErrorActionPreference='Stop' a locked-file delete would
# abort the whole purge mid-run. Mirrors build-run.ps1's kill step.
#
# Symlink guard: skips any reparse-point folder rather than recursing into
# it. PowerShell's `Remove-Item -Recurse` follows symlinks and nukes the
# target — a guard is non-negotiable when scanning blindly.

[CmdletBinding()]
param(
    # Override the target worktree. Defaults to the repo that contains
    # this script copy (so VS Code "Run" on the open file picks the
    # currently-edited worktree).
    [string]$Target,

    # Interactive worktree picker via scripts/lib/menu.psm1. Overrides
    # -Target. Useful when cleaning from a terminal with several
    # worktrees checked out.
    [switch]$Pick,

    # Also delete the released ZIP staging under artifacts\Deckle-v*\.
    # Destructive of release artefacts — opt-in only.
    [switch]$IncludeReleases
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'build-server-cleanup.ps1')
. (Join-Path $LibDir 'deckle-process.ps1')

$Workflow = 'Clean build outputs'
$RepoRoot = $null
$removed = 0
$skipped = 0
$totalBytes = [int64]0
$buildServersStopped = 'Not attempted'
$removedLabels = @()
$skippedLabels = @()
$buildServerCleanup = $null

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Build output cleanup failed before completion." `
        -Details ([ordered]@{
            Worktree          = $RepoRoot
            'Removed folders' = $removed
            'Skipped folders' = $skipped
            'Freed bytes'     = $totalBytes
            'Build servers'   = $buildServersStopped
            Removed           = $removedLabels
            Skipped           = $skippedLabels
            Error             = $_.Exception.Message
        })
    throw
}

# =============================================================================
# RepoRoot resolution — mirrors build-run.ps1 so the two scripts behave
# the same way when called from the same context (VS Code Run, terminal,
# scripts/deckle.ps1 with -Target). Two levels up from this script:
# scripts/commands/clean.ps1 → scripts/ → <repo root>.
# =============================================================================
if ($Pick) {
    Import-Module (Join-Path $LibDir 'menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

# =============================================================================
# Helpers
# =============================================================================

# Compute folder size before deletion — gives a meaningful end-of-run
# tally. Get-ChildItem -Force picks up hidden/system files (e.g. NuGet
# .lock); -ErrorAction SilentlyContinue swallows transient access denials
# on Windows-locked temp files inside obj/.
function Get-FolderSizeBytes {
    param([string]$Path)
    $sum = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue |
           Measure-Object -Property Length -Sum
    if ($sum.Sum) { [int64]$sum.Sum } else { [int64]0 }
}

function Format-Size {
    param([int64]$Bytes)
    if     ($Bytes -ge 1GB) { '{0:N1} GB' -f ($Bytes / 1GB) }
    elseif ($Bytes -ge 1MB) { '{0:N1} MB' -f ($Bytes / 1MB) }
    elseif ($Bytes -ge 1KB) { '{0:N1} KB' -f ($Bytes / 1KB) }
    else                    { "$Bytes B" }
}

# Delete one folder, with size tally and reparse-point guard. Returns a
# hashtable { Removed; Skipped; Bytes; Label } so the caller can accumulate.
function Remove-OutputDir {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )
    $result = @{ Removed = 0; Skipped = 0; Bytes = [int64]0; Label = $Label }
    if (-not (Test-Path -LiteralPath $Path)) { return $result }

    # Symlink / junction guard — don't recurse into a reparse point;
    # Remove-Item would shoot the target on the other side.
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host ("  ! skipped (reparse point): {0}" -f $Label) -ForegroundColor Yellow
        $result.Skipped = 1
        return $result
    }

    $bytes = Get-FolderSizeBytes -Path $Path
    Remove-Item -LiteralPath $Path -Recurse -Force
    $result.Removed = 1
    $result.Bytes   = $bytes
    Write-Host ("  - {0,-44}  ({1})" -f $Label, (Format-Size $bytes)) -ForegroundColor DarkGray
    return $result
}

# =============================================================================
# 0. Stop the running Deckle instance — it locks Deckle.exe + DLLs under
#    artifacts\bin\Deckle.App\, which would make the delete throw and abort.
# =============================================================================
Stop-DeckleProcess

# Stop .NET build servers left by manual, menu, or agent builds. This is
# intentionally machine-wide: MSBuild/Roslyn servers are developer cache
# processes, not repo artifacts, and they are the usual source of lingering
# ".NET Host" rows after a Deckle build.
Write-Host ""
Write-Host "Stopping .NET build servers ..." -ForegroundColor Cyan
$buildServerCleanup = Stop-DotnetBuildServers
if ($buildServerCleanup.Succeeded) {
    $buildServersStopped = $buildServerCleanup.StoppedSummary
    Write-Host "  - dotnet build-server shutdown" -ForegroundColor DarkGray
    Write-Host ("  - before:  {0}" -f $buildServerCleanup.BeforeSummary) -ForegroundColor DarkGray
    Write-Host ("  - stopped: {0}" -f $buildServerCleanup.StoppedSummary) -ForegroundColor DarkGray
    Write-Host ("  - remain:  {0}" -f $buildServerCleanup.RemainingSummary) -ForegroundColor DarkGray
} else {
    $buildServersStopped = "Failed (code $($buildServerCleanup.ExitCode))"
    Write-Host "  ! dotnet build-server shutdown failed (code $($buildServerCleanup.ExitCode))" -ForegroundColor Yellow
}

function Add-Result {
    param($Result)
    $script:removed    += $Result.Removed
    $script:skipped    += $Result.Skipped
    $script:totalBytes += $Result.Bytes
    if ($Result.Removed -gt 0) { $script:removedLabels += $Result.Label }
    if ($Result.Skipped -gt 0) { $script:skippedLabels += $Result.Label }
}

# =============================================================================
# 1. Consolidated artifacts output (src/ + tests/ via the root layout).
#    bin/obj/publish/package are transient; Deckle-v* release staging is
#    kept unless -IncludeReleases.
# =============================================================================
$ArtifactsDir = Join-Path $RepoRoot 'artifacts'
if (Test-Path -LiteralPath $ArtifactsDir) {
    Write-Host ""
    Write-Host "Cleaning artifacts\ (consolidated build output) ..." -ForegroundColor Cyan
    foreach ($name in @('bin', 'obj', 'publish', 'package')) {
        Add-Result (Remove-OutputDir -Path (Join-Path $ArtifactsDir $name) -Label "artifacts\$name")
    }

    if ($IncludeReleases) {
        Write-Host "Cleaning artifacts\Deckle-v* (release staging, -IncludeReleases) ..." -ForegroundColor Cyan
        Get-ChildItem -LiteralPath $ArtifactsDir -Directory -Filter 'Deckle-v*' -ErrorAction SilentlyContinue |
            ForEach-Object { Add-Result (Remove-OutputDir -Path $_.FullName -Label "artifacts\$($_.Name)") }
    }
}

# =============================================================================
# 2. Straggler per-project bin/obj — benchmark/asr studies (classic layout, opts out of
#    the root props) plus any pre-migration leftovers under src/ and tests/.
# =============================================================================
$stragglerRoots = @(
    (Join-Path $RepoRoot 'src'),
    (Join-Path $RepoRoot 'tests'),
    (Join-Path $RepoRoot 'benchmark\asr\studies')
) | Where-Object { Test-Path -LiteralPath $_ }

Write-Host ""
Write-Host "Cleaning straggler bin/ and obj/ under src/, tests/, benchmark/asr/studies/ ..." -ForegroundColor Cyan
foreach ($root in $stragglerRoots) {
    $rootName = Split-Path $root -Leaf
    $rootParent = Split-Path $root -Parent
    if ((Split-Path $rootParent -Leaf) -eq 'asr') { $rootName = "benchmark\asr\$rootName" }
    foreach ($module in (Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
        foreach ($name in @('bin', 'obj')) {
            $dir = Join-Path $module.FullName $name
            Add-Result (Remove-OutputDir -Path $dir -Label "$rootName\$($module.Name)\$name")
        }
    }
}

# =============================================================================
# Tally
# =============================================================================
Write-Host ""
Write-Host ("Done. {0} folder(s) removed, {1} freed." -f $removed, (Format-Size $totalBytes)) -ForegroundColor Green
if ($skipped -gt 0) {
    Write-Host ("Skipped {0} reparse-point folder(s) — inspect manually." -f $skipped) -ForegroundColor Yellow
}
if (-not $IncludeReleases) {
    Write-Host "Kept artifacts\Deckle-v* release staging (pass -IncludeReleases to purge)." -ForegroundColor DarkGray
}

$summaryResult = if ($skipped -gt 0) { 'Partial' } else { 'Success' }
$summarySentence = if ($skipped -gt 0) {
    "Build output cleanup removed $removed folder(s), but skipped $skipped reparse-point folder(s)."
} else {
    "Build output cleanup removed $removed folder(s) and freed $(Format-Size $totalBytes)."
}
$buildServerExit = if (-not $buildServerCleanup) {
    'Unknown'
} elseif ($buildServerCleanup.ExitCode -eq 0) {
    '0'
} elseif ($buildServerCleanup.Succeeded) {
    "$($buildServerCleanup.ExitCode) (no servers remained)"
} else {
    "$($buildServerCleanup.ExitCode)"
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result $summaryResult `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        Worktree          = $RepoRoot
        'Removed folders' = $removed
        Removed           = $(if ($removedLabels.Count -gt 0) { $removedLabels } else { 'None' })
        'Skipped folders' = $skipped
        Skipped           = $(if ($skippedLabels.Count -gt 0) { $skippedLabels } else { 'None' })
        Freed             = (Format-Size $totalBytes)
        'Build servers'   = $buildServersStopped
        'Stopped servers' = $(if ($buildServerCleanup) { $buildServerCleanup.StoppedList } else { 'Unknown' })
        'Still running'   = $(if ($buildServerCleanup) { $buildServerCleanup.RemainingList } else { 'Unknown' })
        'Build server exit' = $buildServerExit
        'Release staging' = $(if ($IncludeReleases) { 'Purged with artifacts\Deckle-v*' } else { 'Kept artifacts\Deckle-v*' })
    })
