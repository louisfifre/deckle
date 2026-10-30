<#
.SYNOPSIS
    Build the app
.DESCRIPTION
    Build Deckle for the chosen configuration (x64) and, unless told not to,
    launch the fresh build. The build-only variant never stops the running
    instance: when it runs from this build's output directory the build is
    refused before compiling.
.NOTES
    section: Run
    order: 120
    action: Build & run |
    action: Build (no run) | -NoRun
#>

[CmdletBinding()]
param(
    # Build only. The running Deckle instance is never stopped; when it runs
    # from this build's output directory the build is refused before compiling,
    # because the assemblies it holds open cannot be replaced.
    [switch]$NoRun,
    [switch]$Wait,
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,
    # Skip the --post-build flag passed to Deckle.exe on launch. The flag
    # triggers a one-shot self-restart at the app side, mitigating the
    # post-build HUD topmost glitch. Disable here for debug scenarios
    # where you want a stable PID (attached debugger, log capture, ...).
    [switch]$NoAutoRestart,
    # Diagnostic branch only: keep this script's build+launch path, but ask
    # Deckle to relaunch into a bounded HUD z-order self-test instead of
    # staying resident.
    [switch]$HudZOrderSelfTest
)

$ErrorActionPreference = 'Stop'
$ScriptDir  = $PSScriptRoot                                  # scripts/commands/
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'deckle-process.ps1')
. (Join-Path $LibDir 'native-console.ps1')

$WorkflowOutput = New-WorkflowOutput -Category 'build'

$Workflow = if ($NoRun) { 'Build' } else { 'Build & run' }
$RepoRoot = $null
$ExePath = $null
$LaunchMode = if ($NoRun) { 'Skipped (-NoRun)' } else { 'Pending' }
$WaitResult = $null

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "$Workflow failed before completion." `
        -Details ([ordered]@{
            Worktree      = $RepoRoot
            Configuration = "$Configuration x64"
            Executable    = $ExePath
            Launch        = $LaunchMode
            Error         = $_.Exception.Message
        })
    throw
}

# The default is the repo holding this script copy, so VS Code's Run on the
# open file builds the worktree being edited.
if ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

$ProjectDir = Join-Path $RepoRoot 'src\Deckle.App'
$Csproj     = Join-Path $ProjectDir 'Deckle.App.csproj'

if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle repo?" }

# The root Directory.Build.props puts every bin/ under
# artifacts\bin\<Project>\<pivot>\; the App's pivot is the lowercased
# configuration (single TFM, and x64 is not a pivot).
$AppArtifactsBin = Join-Path $RepoRoot 'artifacts\bin\Deckle.App'
$PivotPrefix     = $Configuration.ToLowerInvariant()

# Build & run replaces the running instance: Deckle is single-instance (a
# resident mutex rejects a second process), and a build cannot overwrite the
# assemblies it holds open. -NoRun never stops it, so it refuses before
# compiling rather than failing on locked files after a full compile and
# leaving the output directory half-copied.
if ($NoRun) {
    $OutputDir = Join-Path $AppArtifactsBin $PivotPrefix
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Check running Deckle instance'
    $locking = Get-DeckleProcessInDirectory -Directory $OutputDir
    if ($locking.Count -gt 0) {
        $pids = ($locking | ForEach-Object { $_.Id }) -join ', '
        throw "Deckle PID $pids is running from $OutputDir, the directory this $Configuration build writes to, and -NoRun never stops it. Build Debug (separate output directory), build from another worktree, or drop -NoRun to replace the running instance with the fresh build."
    }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "No running Deckle.exe in $OutputDir; any running instance is left untouched" -Role Muted
} else {
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Stop running Deckle instance'
    Stop-DeckleProcess -WriteEvent {
        param([string]$Role, [string]$Message)
        Write-WorkflowMessage -Output $WorkflowOutput -Message $Message -Role $Role
    }
}

# Restore stays implicit (separate evaluation phase before Build), so the
# WindowsAppSDK targets (CompileXaml etc.) get imported from the regenerated
# .nuget.g.targets — no CS5001 / CS0103 InitializeComponent surprise in a fresh
# worktree. /m:4 stays explicit: the .NET CLI applies its default node count
# (every logical CPU) after Directory.Build.rsp. Four nodes measured as the
# knee on 2026-10-14: eight gained nothing, one doubled the wall time.
Write-WorkflowStep -Output $WorkflowOutput -Message "dotnet build ($Configuration x64)"
$buildExitCode = Invoke-CapturedProcess -FilePath 'dotnet' -ArgumentList @(
    'build', $Csproj, "-c:$Configuration", '-p:Platform=x64', '-v:m', '-nologo',
    '/m:4'
)
if ($buildExitCode -ne 0) { throw "dotnet build failed (code $buildExitCode)" }
Write-WorkflowResult -Output $WorkflowOutput -Message 'Build succeeded'

if ($NoRun) {
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Done'
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'Launch skipped because -NoRun was set'
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Deckle was built in $Configuration x64; launch was skipped and the running instance was left untouched." `
        -Details ([ordered]@{
            Worktree           = $RepoRoot
            Configuration      = "$Configuration x64"
            Executable         = 'Not resolved (-NoRun)'
            Launch             = $LaunchMode
            'Running instance' = 'Left untouched'
        })
    return
}

# Prefix match on the pivot (debug, debug_win-x64) rather than an exact folder,
# so a RID-suffixed pivot cannot strand the launch on a stale exe; the newest
# write wins when several pivots coexist.
Write-WorkflowStep -Output $WorkflowOutput -Message 'Resolve built executable'
$ExeCandidates = Get-ChildItem -Path $AppArtifactsBin -Recurse -Filter 'Deckle.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Name -like "$PivotPrefix*" } |
    Sort-Object LastWriteTime -Descending
if (-not $ExeCandidates) { throw "Exe not found under $AppArtifactsBin (expected artifacts\bin\Deckle.App\$PivotPrefix\Deckle.exe)" }
$ExePath = $ExeCandidates[0].FullName
Write-WorkflowMessage -Output $WorkflowOutput -Message $ExePath

function Start-DeckleViaShell {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [string[]]$DeckleArgs = @()
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $true
    $psi.WorkingDirectory = Split-Path -Parent $FilePath
    $psi.Arguments = ($DeckleArgs -join ' ')
    [System.Diagnostics.Process]::Start($psi) | Out-Null
}

# Real ShellExecute detaches Deckle from PowerShell and gives it the shell path
# of a user-opened executable; `cmd /c start` goes through a console shell and
# leaves Deckle under the build script's foreground state, which breaks the HUD
# topmost. The first launch after a build can still inherit a degraded
# foreground state, so --post-build lets the app relaunch itself once through
# the same ShellExecute primitive.
$launchArgs = @()
if ($HudZOrderSelfTest) {
    $launchArgs = @('--post-build-hud-zorder-selftest')
} elseif ($NoAutoRestart) {
    $launchArgs = @()
} else {
    $launchArgs = @('--post-build')
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'Launch Deckle'
Start-DeckleViaShell -FilePath $ExePath -DeckleArgs $launchArgs
if ($launchArgs.Count) {
    Write-WorkflowResult -Output $WorkflowOutput -Message ("Started with args: {0}" -f ($launchArgs -join ' '))
    $LaunchMode = "Started with args: $($launchArgs -join ' ')"
} else {
    Write-WorkflowResult -Output $WorkflowOutput -Message 'Started without post-build restart args'
    $LaunchMode = 'Started without post-build restart args'
}

if ($Wait) {
    # The shell launch keeps no process handle, so poll for the new process;
    # the 5 s ceiling keeps a failed start from hanging the script.
    $deadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $proc = Get-Process -Name Deckle -ErrorAction SilentlyContinue | Select-Object -First 1
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
    -Sentence "Deckle was built in $Configuration x64 and launched from this worktree." `
    -Details ([ordered]@{
        Worktree      = $RepoRoot
        Configuration = "$Configuration x64"
        Executable    = $ExePath
        Launch        = $LaunchMode
        Wait          = $WaitResult
    })
