$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$CommandDir = Join-Path $ScriptsDir 'commands'
$LibDir = Join-Path $ScriptsDir 'lib'
$LauncherDir = Join-Path $LibDir 'launcher'
$MenuDir = Join-Path $LibDir 'menu'
. (Join-Path $LauncherDir 'statistics-plans.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected '$Expected', got '$Actual'" }
}

function Assert-Contains([string[]]$Lines, [string]$Expected, [string]$Case) {
    if (($Lines -join "`n") -notlike "*$Expected*") { throw "${Case}: missing '$Expected'" }
}

function Assert-Throws([scriptblock]$Action, [string]$Expected, [string]$Case) {
    try {
        & $Action
        throw "${Case}: expected an exception"
    } catch {
        if ($_.Exception.Message -notlike "*$Expected*") {
            throw "${Case}: expected '$Expected', got '$($_.Exception.Message)'"
        }
    }
}

$repositoryGoals = @(Get-MaintenanceScanGoals -Kind Repository)
Assert-Equal 4 $repositoryGoals.Count 'repository goal count'
Assert-Equal 'Files to review' $repositoryGoals[1].Label 'repository goal is outcome-oriented'
$overview = New-MaintenanceScanSpecification -Kind Repository -Goal overview
Assert-Equal 'Files Bytes' (@($overview.Measurements) -join ' ') 'overview stays metadata-only'

$contextGoals = @(Get-MaintenanceScanGoals -Kind Context)
Assert-Equal 3 $contextGoals.Count 'context goal count'
Assert-Equal 'Recent changes' $contextGoals[1].Label 'context goal does not promise semantic drift'

$reviewSpec = New-MaintenanceScanSpecification -Kind Repository -Goal files-to-review
Assert-Equal 'Standard' $reviewSpec.ThresholdProfile 'review preset uses explicit limits'
Assert-Equal 'Findings' $reviewSpec.Detail 'review preset requests findings'
$reviewLines = @(Get-MaintenanceScanReviewLines -Specification $reviewSpec -Worktree 'D:\repo')
Assert-Contains $reviewLines 'Goal         Files to review' 'review identifies goal'
Assert-Contains $reviewLines '256 KB / 1 MB' 'review exposes concrete threshold values'
Assert-Contains $reviewLines 'Read-only' 'review states the deletion boundary'

$customCopy = Copy-MaintenanceScanSpecification -Specification $reviewSpec
Assert-Equal 'custom' $customCopy.Goal 'editing a preset uses the same custom specification'
Assert-Equal 'Standard' $customCopy.ThresholdProfile 'copy keeps preset values'

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('deckle-statistics-plan-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'src') | Out-Null
    $customCopy.ScopePath = 'src/.'
    $resolved = Resolve-MaintenanceScanSpecification -Specification $customCopy -Worktree $fixture
    Assert-Equal 'src' $resolved.ScopePath 'scope is normalized after the worktree is known'

    $customCopy.ScopePath = '..\outside'
    Assert-Throws { Resolve-MaintenanceScanSpecification -Specification $customCopy -Worktree $fixture } 'cannot leave' 'parent traversal is rejected'
    $customCopy.ScopePath = '.git'
    Assert-Throws { Resolve-MaintenanceScanSpecification -Specification $customCopy -Worktree $fixture } '.git' 'git metadata is rejected'
    $customCopy.ScopePath = 'AppData.lnk'
    Assert-Throws { Resolve-MaintenanceScanSpecification -Specification $customCopy -Worktree $fixture } 'AppData.lnk' 'explicit shortcut boundary'
    $customCopy.ScopePath = 'missing'
    Assert-Throws { Resolve-MaintenanceScanSpecification -Specification $customCopy -Worktree $fixture } 'does not exist' 'unknown custom scope is reviewed before launch'
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}

Write-Host 'statistics-plans.tests.ps1: PASS' -ForegroundColor Green
