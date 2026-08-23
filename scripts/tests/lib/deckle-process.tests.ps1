$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'deckle-process.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

$script:RunningDeckle = @()
function Get-Process { param($Name, $ErrorAction) return $script:RunningDeckle }

$script:ProcessEvents = [System.Collections.Generic.List[object]]::new()
Stop-DeckleProcess -WriteEvent {
    param([string]$Role, [string]$Message)
    $script:ProcessEvents.Add([pscustomobject]@{ Role = $Role; Message = $Message })
}

Assert-Equal 1 $script:ProcessEvents.Count 'an idle process check emits one result'
Assert-Equal 'Muted' $script:ProcessEvents[0].Role 'no running instance is neutral rather than successful'
Assert-Equal 'No running Deckle.exe found' $script:ProcessEvents[0].Message 'the idle result remains explicit'

# A build only conflicts with the instance launched from its own output
# directory: the same pivot in another worktree, a RID-suffixed sibling pivot,
# and a process whose path cannot be read all leave the build free to write.
$releaseDir = 'D:\repo\artifacts\bin\Deckle.App\release'
$script:RunningDeckle = @(
    [pscustomobject]@{ Id = 101; Path = "$releaseDir\Deckle.exe" }
    [pscustomobject]@{ Id = 202; Path = 'D:\repo\artifacts\bin\Deckle.App\release_win-x64\Deckle.exe' }
    [pscustomobject]@{ Id = 303; Path = 'D:\worktrees\repo\subject\artifacts\bin\Deckle.App\release\Deckle.exe' }
    [pscustomobject]@{ Id = 404; Path = $null }
)

$inRelease = @(Get-DeckleProcessInDirectory -Directory $releaseDir)
Assert-Equal 1 $inRelease.Count 'only the instance launched from the build output directory is reported'
Assert-Equal 101 $inRelease[0].Id 'the reported instance is the one living in that directory'

$inDebug = @(Get-DeckleProcessInDirectory -Directory 'D:\repo\artifacts\bin\Deckle.App\debug')
Assert-Equal 0 $inDebug.Count 'a build into another pivot directory has no locking instance'

$inReleaseSpelledDifferently = @(Get-DeckleProcessInDirectory -Directory 'D:\REPO\artifacts\bin\Deckle.App\Release\')
Assert-Equal 1 $inReleaseSpelledDifferently.Count 'directory matching ignores case and a trailing separator'

$script:RunningDeckle = @()
$whenIdle = @(Get-DeckleProcessInDirectory -Directory $releaseDir)
Assert-Equal 0 $whenIdle.Count 'no running instance means no locking instance'

Write-Host 'deckle-process.tests.ps1: PASS' -ForegroundColor Green
