[CmdletBinding()]
param(
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    [string]$Target,
    [switch]$Pick,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'deckle-process.ps1')

$WorkflowOutput = New-DeckleWorkflowOutput -Category 'launch'

$Workflow = 'Launch'
$RepoRoot = $null
$ExePath = $null
$WaitResult = $null

trap {
    Write-DeckleActionSummary `
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

if ($Pick) {
    Import-Module (Join-Path $LibDir 'menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-DeckleOutputText -Text "Repo: $RepoRoot" -Role Muted

$AppArtifactsBin = Join-Path $RepoRoot 'artifacts\bin\Deckle.App'
$PivotPrefix     = $Configuration.ToLowerInvariant()

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Stop running Deckle instance'
Stop-DeckleProcess -WriteEvent {
    param([string]$Role, [string]$Message)
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message $Message -Role $Role
}

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Resolve built executable ($Configuration)"
$ExeCandidates = Get-ChildItem -Path $AppArtifactsBin -Recurse -Filter 'Deckle.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -like "$PivotPrefix*" } |
    Sort-Object LastWriteTime -Descending

if (-not $ExeCandidates) {
    throw "Exe not found under $AppArtifactsBin. Build $Configuration first."
}

$ExePath = $ExeCandidates[0].FullName
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message $ExePath

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $ExePath
$psi.UseShellExecute = $true
$psi.WorkingDirectory = Split-Path -Parent $ExePath

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Launch Deckle'
[System.Diagnostics.Process]::Start($psi) | Out-Null
Write-DeckleWorkflowResult -Output $WorkflowOutput -Message 'Started without build'

if ($Wait) {
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $proc = Get-Process -Name Deckle -ErrorAction SilentlyContinue | Select-Object -First 1
    } while (-not $proc -and (Get-Date) -lt $deadline)
    if ($proc) {
        Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Wait for Deckle PID $($proc.Id)"
        $proc.WaitForExit()
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Deckle exited with code $($proc.ExitCode)"
        $WaitResult = "Deckle PID $($proc.Id) exited with code $($proc.ExitCode)"
    } else {
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'Deckle process did not appear within 5 seconds' -Role Warning
        $WaitResult = 'Deckle process did not appear within 5 seconds'
    }
}

Write-DeckleActionSummary `
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
