<#
.SYNOPSIS
    Stop .NET build servers
.DESCRIPTION
    Stops the MSBuild and Roslyn build servers left behind by dotnet builds,
    which Task Manager lists as ".NET Host" and "VBCSCompiler". They speed up
    follow-up builds, but repeated builds across worktrees can accumulate
    enough of them to slow the machine down. The servers belong to the
    machine, not to a repository, so every one is stopped, and a build in
    progress is interrupted. There is no preview: the script acts immediately.
.NOTES
    section: .NET
    order: 100
    action: Stop .NET build servers | | danger | Stops every MSBuild and Roslyn build server of the machine, which interrupts a build in progress.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$LibDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'build-server-cleanup.ps1')

$Workflow = 'Stop .NET build servers'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Stopping .NET build servers failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

$output = New-WorkflowOutput -Category 'dotnet'

Write-WorkflowStep -Output $output -Message 'Stop the .NET build servers'
$cleanup = Stop-DotnetBuildServers
if (-not $cleanup.Succeeded) {
    throw "dotnet build-server shutdown failed (code $($cleanup.ExitCode))"
}

Write-WorkflowMessage -Output $output -Message 'dotnet build-server shutdown' -Role Muted
Write-WorkflowMessage -Output $output -Message ("before:  {0}" -f $cleanup.BeforeSummary) -Role Muted
Write-WorkflowMessage -Output $output -Message ("stopped: {0}" -f $cleanup.StoppedSummary) -Role Muted
Write-WorkflowMessage -Output $output -Message ("remain:  {0}" -f $cleanup.RemainingSummary) -Role Muted

$summarySentence = if ($cleanup.BeforeCount -eq 0) {
    'No .NET build servers were running.'
} elseif ($cleanup.RemainingCount -eq 0) {
    "Stopped $($cleanup.StoppedCount) .NET build server process(es)."
} else {
    "Stopped $($cleanup.StoppedCount) .NET build server process(es); $($cleanup.RemainingCount) still running."
}
# The command exits non-zero when no server was left to stop, which is not a failure.
$commandExit = if ($cleanup.ExitCode -eq 0) { '0' } else { "$($cleanup.ExitCode) (no servers remained)" }

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        Before          = $cleanup.BeforeSummary
        Stopped         = $cleanup.StoppedSummary
        Processes       = $cleanup.StoppedList
        Remaining       = $cleanup.RemainingSummary
        'Still running' = $cleanup.RemainingList
        'Command exit'  = $commandExit
        Command         = 'dotnet build-server shutdown'
    })
