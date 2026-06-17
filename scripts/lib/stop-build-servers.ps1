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
. (Join-Path $ScriptDir 'action-summary.ps1')

$Workflow = 'Stop .NET build servers'

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
& dotnet build-server shutdown
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build-server shutdown failed (code $LASTEXITCODE)"
}

Write-Host "  - dotnet build-server shutdown" -ForegroundColor DarkGray

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Stopped .NET build servers left by local, menu, or agent builds." `
    -Details ([ordered]@{
        Command = 'dotnet build-server shutdown'
    })
