$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$root = Join-Path ([IO.Path]::GetTempPath()) "deckle-build-watchdog-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $root

# Without -Arm or -Run the script only defines its functions.
. (Join-Path $ScriptsDir 'build-server-watchdog.ps1') -StateDir $root

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw "${Case}: expected $Expected, got $Actual" }
}

try {
    $now = Get-Date '2026-11-06 15:00:00'

    # The servers stay while builds keep coming, and go once five minutes have
    # passed since the last project build.
    Assert-Equal 'Wait' (Get-BuildServerIdleDecision -LastBuild $now.AddMinutes(-4) -Now $now -IdleMinutes 5) 'a build four minutes ago keeps the servers'
    Assert-Equal 'Shutdown' (Get-BuildServerIdleDecision -LastBuild $now.AddMinutes(-5) -Now $now -IdleMinutes 5) 'five idle minutes shut the servers down'
    Assert-Equal 'Shutdown' (Get-BuildServerIdleDecision -LastBuild $now.AddHours(-2) -Now $now -IdleMinutes 5) 'a long idle period shuts the servers down'
    Assert-Equal 'Wait' (Get-BuildServerIdleDecision -LastBuild $now.AddHours(-2) -Now $now -IdleMinutes 5 -BuildLockHeld $true) 'a build holding the lock is never interrupted'

    # The newest stamp dates the last build; no stamp counts as a build now.
    Assert-Equal $now (Get-LastBuildTime -StateDir $root -Now $now) 'no stamp means the delay starts now'
    $older = New-Item -ItemType File -Path (Join-Path $root 'Deckle.Core.stamp')
    $older.LastWriteTime = $now.AddMinutes(-30)
    $newer = New-Item -ItemType File -Path (Join-Path $root 'Deckle.App.stamp')
    $newer.LastWriteTime = $now.AddMinutes(-3)
    Assert-Equal $now.AddMinutes(-3) (Get-LastBuildTime -StateDir $root -Now $now) 'the newest stamp is the last build'

    # A heartbeat proves a live watchdog only while it is fresh.
    $heartbeat = Join-Path $root 'watchdog.heartbeat'
    Assert-Equal $false (Test-WatchdogAlive -HeartbeatPath $heartbeat -Now $now -StaleSeconds 120) 'no heartbeat means no watchdog'
    $beat = New-Item -ItemType File -Path $heartbeat
    $beat.LastWriteTime = $now.AddSeconds(-30)
    Assert-Equal $true (Test-WatchdogAlive -HeartbeatPath $heartbeat -Now $now -StaleSeconds 120) 'a fresh heartbeat means a live watchdog'
    $beat.LastWriteTime = $now.AddSeconds(-121)
    Assert-Equal $false (Test-WatchdogAlive -HeartbeatPath $heartbeat -Now $now -StaleSeconds 120) 'a stale heartbeat means the watchdog is gone'

    # MSBuild and the script agree on what stale means.
    Assert-Equal 120 (Get-HeartbeatStaleSeconds -TickSeconds 30) 'four ticks of thirty seconds match the 120 s in Directory.Build.targets'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'build-server-watchdog.tests.ps1: PASS' -ForegroundColor Green
