# Reproduce the logon situation without logging off: stop Windhawk (mods unload, stock bar),
# then relaunch it in -tray-only so it injects into the already-running explorer ("late injection").
# Measures the taskbar buttons by UI Automation before, between and after. Read-only apart from Windhawk itself.
param([int]$SettleSeconds = 20)
$ErrorActionPreference = 'Continue'
$wh = 'D:\bin\scoop\apps\windhawk\current\windhawk.exe'
$uia = Join-Path $PSScriptRoot 'tray-uia.ps1'
$probe = Join-Path $PSScriptRoot 'taskbar-probe.ps1'

function Snapshot([string]$label) {
    "== $label  $(Get-Date -Format 'HH:mm:ss')"
    & $probe -Label $label 2>$null | Where-Object { $_ -like 'Shell_TrayWnd*' -or $_ -like 'windhawk modules*' }
    & $uia | Where-Object { $_ -match 'pinned|running window|StartButton|TaskViewButton' }
}

Snapshot 'before (Windhawk running)'

'== windhawk -exit -wait'
& $wh -exit -wait
Start-Sleep -Seconds 6
Snapshot 'after exit (stock bar expected)'

'== windhawk -tray-only (late injection)'
& $wh -tray-only
Start-Sleep -Seconds $SettleSeconds
Snapshot "after late injection (+$SettleSeconds s)"
