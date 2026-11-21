<#
.SYNOPSIS
    Launch the freshest built app
.DESCRIPTION
    Replace the running Deckle instance with the freshest already-built
    executable of the chosen configuration. No build happens here. The running
    instance is stopped first: Deckle is single-instance, so the new process
    could not take over while the old one holds the resident mutex.
    -App mail or atelier launches DeckleMail.exe or DeckleAtelier.exe instead:
    separate apps with their own resident mutex and data root, so the running
    Deckle is never stopped (only a previous instance of the same small app is).
.NOTES
    section: Run
    order: 110
    action: Launch |
    action: Launch Mail | -App mail
    action: Launch Folders | -App atelier
#>

[CmdletBinding()]
param(
    # Which app of the suite: deckle (the published Deckle), mail or atelier.
    # deckle, mail or atelier; checked below, not a [ValidateSet]: the script's one
    # enumerated parameter is Configuration, which the launcher reads as its variant.
    [string]$App = 'deckle',
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    [string]$Target,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
if ($App -notin 'deckle', 'mail', 'atelier') { [Console]::Error.WriteLine("-App is deckle, mail or atelier, not '$App'."); exit 1 }
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path $ScriptDir 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'deckle-process.ps1')

$WorkflowOutput = New-WorkflowOutput -Category 'launch'

$Workflow = 'Launch'
$RepoRoot = $null
$ExePath = $null
$WaitResult = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Deckle launch failed before completion." `
        -Details ([ordered]@{
            Worktree      = $RepoRoot
            Configuration = $Configuration
            Executable    = $ExePath
            Error         = $_.Exception.Message
        })
    throw
}

if ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent $ScriptDir
}

Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

$AppProject  = @{ deckle = 'Deckle.App'; mail = 'Deckle.Mail.App'; atelier = 'Deckle.Atelier.App' }[$App]
$ExeName     = @{ deckle = 'Deckle.exe'; mail = 'DeckleMail.exe'; atelier = 'DeckleAtelier.exe' }[$App]
$ProcessName = [System.IO.Path]::GetFileNameWithoutExtension($ExeName)
$AppArtifactsBin = Join-Path $RepoRoot "artifacts\bin\$AppProject"
$PivotPrefix     = $Configuration.ToLowerInvariant()

Write-WorkflowStep -Output $WorkflowOutput -Message "Stop running $ProcessName instance"
Stop-DeckleProcess -ProcessName $ProcessName -WriteEvent {
    param([string]$Role, [string]$Message)
    Write-WorkflowMessage -Output $WorkflowOutput -Message $Message -Role $Role
}

Write-WorkflowStep -Output $WorkflowOutput -Message "Resolve built executable ($Configuration)"
$ExeCandidates = Get-ChildItem -Path $AppArtifactsBin -Recurse -Filter $ExeName -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -like "$PivotPrefix*" } |
    Sort-Object LastWriteTime -Descending

if (-not $ExeCandidates) {
    throw "Exe not found under $AppArtifactsBin. Build $Configuration first."
}

$ExePath = $ExeCandidates[0].FullName
Write-WorkflowMessage -Output $WorkflowOutput -Message $ExePath

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $ExePath
$psi.UseShellExecute = $true
$psi.WorkingDirectory = Split-Path -Parent $ExePath

Write-WorkflowStep -Output $WorkflowOutput -Message 'Launch Deckle'
[System.Diagnostics.Process]::Start($psi) | Out-Null
Write-WorkflowResult -Output $WorkflowOutput -Message 'Started without build'

if ($Wait) {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    } while (-not $proc -and (Get-Date) -lt $deadline)
    if ($proc) {
        Write-WorkflowStep -Output $WorkflowOutput -Message "Wait for Deckle PID $($proc.Id)"
        $proc.WaitForExit()
        Write-WorkflowMessage -Output $WorkflowOutput -Message "Deckle exited with code $($proc.ExitCode)"
        $WaitResult = "Deckle PID $($proc.Id) exited with code $($proc.ExitCode)"
    } else {
        Write-WorkflowMessage -Output $WorkflowOutput -Message 'Deckle process did not appear within 5 seconds' -Role Warning
        $WaitResult = 'Deckle process did not appear within 5 seconds'
    }
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence "Deckle was launched from the existing $Configuration build; no build was run." `
    -Details ([ordered]@{
        Worktree      = $RepoRoot
        Configuration = $Configuration
        Executable    = $ExePath
        Launch        = 'Started without build'
        Wait          = $WaitResult
    })
