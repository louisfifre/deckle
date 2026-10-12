# Capture the taskbar state during the first minute after logon, before touching anything.
# Run it right after logging on (a shortcut, or a logon scheduled task). It appends UI Automation
# snapshots of the taskbar buttons and the Windhawk module count to a log file at fixed offsets,
# so the moment when pinned buttons diverge from running ones can be read afterwards.
# Read-only: it never changes Windhawk, explorer or the registry.
param(
    [string]$LogFile = (Join-Path $env:LOCALAPPDATA 'deckle-taskbar-logon.log'),
    [int[]]$OffsetsSeconds = @(0, 10, 20, 40, 60, 120)
)
$ErrorActionPreference = 'Continue'
$uia = Join-Path $PSScriptRoot 'tray-uia.ps1'
$probe = Join-Path $PSScriptRoot 'taskbar-probe.ps1'
$start = Get-Date

function Snapshot([string]$label) {
    $lines = @("== $label  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    $explorer = Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -First 1
    $windhawk = Get-Process windhawk -ErrorAction SilentlyContinue | Select-Object -First 1
    $lines += "explorer started=$($explorer.StartTime) windhawk started=$($windhawk.StartTime)"
    $lines += (& $probe -Label $label 2>$null | Where-Object { $_ -like 'Shell_TrayWnd*' -or $_ -like 'windhawk modules*' })
    $lines += (& $uia 2>$null | Where-Object { $_ -match 'pinned|running window|StartButton|TaskViewButton|NotifyItemIcon' })
    Add-Content -Path $LogFile -Value $lines -Encoding utf8
}

Add-Content -Path $LogFile -Value "######## logon capture started $($start.ToString('yyyy-MM-dd HH:mm:ss'))" -Encoding utf8
foreach ($offset in $OffsetsSeconds) {
    $wait = $offset - ((Get-Date) - $start).TotalSeconds
    if ($wait -gt 0) { Start-Sleep -Seconds ([math]::Ceiling($wait)) }
    Snapshot "+$offset s"
}
"written to $LogFile"
