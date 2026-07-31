$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'deckle-process.ps1')

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

function Get-Process { param($Name, $ErrorAction) return @() }

$script:ProcessEvents = [System.Collections.Generic.List[object]]::new()
Stop-DeckleProcess -WriteEvent {
    param([string]$Role, [string]$Message)
    $script:ProcessEvents.Add([pscustomobject]@{ Role = $Role; Message = $Message })
}

Assert-Equal 1 $script:ProcessEvents.Count 'an idle process check emits one result'
Assert-Equal 'Muted' $script:ProcessEvents[0].Role 'no running instance is neutral rather than successful'
Assert-Equal 'No running Deckle.exe found' $script:ProcessEvents[0].Message 'the idle result remains explicit'

Write-Host 'deckle-process.tests.ps1: PASS' -ForegroundColor Green
