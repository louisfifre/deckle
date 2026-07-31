# stop-build-servers.ps1 — Stop .NET build cache processes
#
# Stops MSBuild/Roslyn build servers left behind by dotnet builds. These show
# up in Task Manager as ".NET Host" and "VBCSCompiler" processes. They speed up
# follow-up builds, but repeated agent/worktree builds can accumulate enough of
# them to hurt Windows and WinUI responsiveness.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'build-server-cleanup.ps1')

$Workflow = 'Stop .NET build servers'
$cleanup = $null

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Stopping .NET build servers failed." `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

Write-Host "Stopping .NET build servers ..." -ForegroundColor Cyan
$cleanup = Stop-DotnetBuildServers
if (-not $cleanup.Succeeded) {
    throw "dotnet build-server shutdown failed (code $($cleanup.ExitCode))"
}

Write-Host "  - dotnet build-server shutdown" -ForegroundColor DarkGray
Write-Host ("  - before:  {0}" -f $cleanup.BeforeSummary) -ForegroundColor DarkGray
Write-Host ("  - stopped: {0}" -f $cleanup.StoppedSummary) -ForegroundColor DarkGray
Write-Host ("  - remain:  {0}" -f $cleanup.RemainingSummary) -ForegroundColor DarkGray

$summarySentence = if ($cleanup.BeforeCount -eq 0) {
    "No .NET build servers were running."
} elseif ($cleanup.RemainingCount -eq 0) {
    "Stopped $($cleanup.StoppedCount) .NET build server process(es)."
} else {
    "Stopped $($cleanup.StoppedCount) .NET build server process(es); $($cleanup.RemainingCount) still running."
}
$commandExit = if ($cleanup.ExitCode -eq 0) { '0' } else { "$($cleanup.ExitCode) (no servers remained)" }

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        Before    = $cleanup.BeforeSummary
        Stopped   = $cleanup.StoppedSummary
        Processes = $cleanup.StoppedList
        Remaining = $cleanup.RemainingSummary
        'Still running' = $cleanup.RemainingList
        'Command exit' = $commandExit
        Command   = 'dotnet build-server shutdown'
    })
