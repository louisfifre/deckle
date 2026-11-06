# build-server-watchdog.ps1 - Shut the .NET build servers down after five idle minutes.
#
# Directory.Build.rsp already stops MSBuild worker nodes from outliving a build,
# but VBCSCompiler, the Roslyn compiler server, keeps running ten minutes after
# its last compilation and, when it hangs, forever: one such server kept a CPU
# core at 100 % for hours, fans at full speed. The SDK's own timeouts cannot be
# set from a repository (the keepalive lives in the SDK's own config file), so
# Directory.Build.targets arms this watchdog at the end of every project build.
# Builds in a burst keep reusing the servers; five minutes after the last one,
# `dotnet build-server shutdown` stops them all, a hung server included.
#
# The state directory carries the state, so builds from every entry point
# (CLI, scripts, agents, worktrees) share one watchdog:
#   <Project>.stamp     touched by MSBuild when that project's Build target
#                       ends; one file per project, so four parallel nodes
#                       never fight over the same file
#   watchdog.heartbeat  touched by the running watchdog at every tick
# MSBuild arms a new watchdog only when the heartbeat is missing or stale, so a
# watchdog lost to a reboot or a kill is replaced at the next build.
#
#   -Arm   start a hidden watchdog process and return at once (MSBuild's call)
#   -Run   the watchdog loop itself
# Without either switch the script only defines its functions, for the tests.
#
# No Header on purpose: the menu has nothing to offer here, MSBuild is the caller.

[CmdletBinding()]
param(
    # Where the stamp and the heartbeat live. One directory per user, shared by
    # every checkout, because the build servers belong to the machine.
    [string]$StateDir = (Join-Path ([IO.Path]::GetTempPath()) 'Deckle.Build'),
    [switch]$Arm,
    [switch]$Run,
    # Minutes without a project build before the servers are shut down.
    [int]$IdleMinutes = 5,
    # Seconds between two checks of the stamp. Also the heartbeat period.
    [int]$TickSeconds = 30
)

$ErrorActionPreference = 'Stop'

$HeartbeatPath = Join-Path $StateDir 'watchdog.heartbeat'

# The newest stamp is the last project build. No stamp at all (a state
# directory wiped while the watchdog ran) counts as a build right now, so the
# servers get their full idle delay rather than an immediate shutdown.
function Get-LastBuildTime {
    param(
        [Parameter(Mandatory)][string]$StateDir,
        [Parameter(Mandatory)][datetime]$Now
    )

    $stamps = @(Get-ChildItem -LiteralPath $StateDir -Filter '*.stamp' -File -ErrorAction SilentlyContinue)
    if ($stamps.Count -eq 0) { return $Now }
    return ($stamps | Measure-Object -Property LastWriteTime -Maximum).Maximum
}

# A heartbeat older than four ticks means the watchdog that wrote it is gone;
# generous enough that a busy machine never breeds a second watchdog.
function Get-HeartbeatStaleSeconds {
    param([int]$TickSeconds)
    return 4 * $TickSeconds
}

# Decides what the watchdog does at one tick. Pure, so the rule is testable:
# wait while a build finished a project less than IdleMinutes ago, wait while
# build-locked.ps1 holds the build lock (a build in progress that has not
# finished a project yet), otherwise shut the servers down.
function Get-BuildServerIdleDecision {
    param(
        [Parameter(Mandatory)][datetime]$LastBuild,
        [Parameter(Mandatory)][datetime]$Now,
        [Parameter(Mandatory)][int]$IdleMinutes,
        [bool]$BuildLockHeld = $false
    )

    if ($BuildLockHeld) { return 'Wait' }
    if (($Now - $LastBuild).TotalMinutes -lt $IdleMinutes) { return 'Wait' }
    return 'Shutdown'
}

function Test-WatchdogAlive {
    param(
        [Parameter(Mandatory)][string]$HeartbeatPath,
        [Parameter(Mandatory)][datetime]$Now,
        [Parameter(Mandatory)][int]$StaleSeconds
    )

    if (-not (Test-Path -LiteralPath $HeartbeatPath)) { return $false }
    $age = $Now - (Get-Item -LiteralPath $HeartbeatPath).LastWriteTime
    return $age.TotalSeconds -lt $StaleSeconds
}

# True when build-locked.ps1 is holding the machine-wide build mutex. Taking
# it for an instant is the only way to ask; an abandoned mutex (a killed build)
# counts as free, exactly as build-locked.ps1 treats it.
function Test-BuildLockHeld {
    $mutex = [System.Threading.Mutex]::new($false, 'Global\Deckle.Build')
    try {
        $acquired = $false
        try {
            $acquired = $mutex.WaitOne(0)
        } catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if ($acquired) { $mutex.ReleaseMutex() }
        return -not $acquired
    } finally {
        $mutex.Dispose()
    }
}

function Write-Heartbeat {
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    [IO.File]::WriteAllText($HeartbeatPath, [string]$PID)
}

function Invoke-WatchdogLoop {
    $staleSeconds = Get-HeartbeatStaleSeconds -TickSeconds $TickSeconds
    try {
        while ($true) {
            Write-Heartbeat
            Start-Sleep -Seconds $TickSeconds

            # Another watchdog took the heartbeat over (two armed in the same
            # instant): the newest one lives, this one leaves.
            if ((Test-Path -LiteralPath $HeartbeatPath) -and
                ([IO.File]::ReadAllText($HeartbeatPath) -ne [string]$PID)) { return }

            $now = Get-Date
            $decision = Get-BuildServerIdleDecision -LastBuild (Get-LastBuildTime -StateDir $StateDir -Now $now) `
                -Now $now -IdleMinutes $IdleMinutes -BuildLockHeld (Test-BuildLockHeld)
            if ($decision -ne 'Shutdown') { continue }

            # The command exits non-zero when no server was running; either way
            # the job is done.
            & dotnet build-server shutdown *> $null
            return
        }
    } finally {
        if ((Test-Path -LiteralPath $HeartbeatPath) -and
            ([IO.File]::ReadAllText($HeartbeatPath) -eq [string]$PID)) {
            Remove-Item -LiteralPath $HeartbeatPath -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Arm) {
    $staleSeconds = Get-HeartbeatStaleSeconds -TickSeconds $TickSeconds
    if (Test-WatchdogAlive -HeartbeatPath $HeartbeatPath -Now (Get-Date) -StaleSeconds $staleSeconds) { exit 0 }

    # Start-Process without handle inheritance, so MSBuild's Exec is not kept
    # waiting on an output pipe the watchdog would hold for minutes.
    Start-Process -FilePath 'pwsh' -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-NonInteractive', '-File', "`"$PSCommandPath`"",
        '-Run', '-StateDir', "`"$StateDir`"",
        '-IdleMinutes', $IdleMinutes, '-TickSeconds', $TickSeconds
    )
    exit 0
}

if ($Run) {
    Invoke-WatchdogLoop
    exit 0
}
