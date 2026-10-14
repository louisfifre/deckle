# Native taskbar test, to run once the feature flags 59213768 and 61161244 report Enabled
# (feature-flags.ps1 -Query). Stops Windhawk so no mod confounds the result, sets the native
# position and size values, restarts explorer, measures by UI Automation, then leaves the native
# bar on screen. Pass -Restore to remove the values, restart explorer and relaunch Windhawk.
# The 2026-10-08 regional-format and IsPromoted probes were dropped: both were established inert.
param([switch]$Restore)
$ErrorActionPreference = 'Continue'
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$wh = 'D:\bin\scoop\apps\windhawk\current\windhawk.exe'
$probe = Join-Path $PSScriptRoot 'taskbar-probe.ps1'
$uia = Join-Path $PSScriptRoot 'tray-uia.ps1'
$flags = Join-Path $PSScriptRoot 'feature-flags.ps1'

function Restart-Explorer {
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 5
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process (Join-Path $env:WINDIR 'explorer.exe') }
    Start-Sleep -Seconds 10
}

if ($Restore) {
    '== restore: remove native values, restart explorer, relaunch Windhawk'
    Remove-ItemProperty -Path $adv -Name TaskbarLocation -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $adv -Name TaskbarSize -ErrorAction SilentlyContinue
    Restart-Explorer
    & $wh -tray-only
    Start-Sleep -Seconds 20
    & $probe -Label restored 2>$null | Where-Object { $_ -like 'Shell_TrayWnd*' -or $_ -like 'windhawk modules*' }
    return
}

'== flags'
& $flags -Query -Ids 59213768,61161244

'== stop Windhawk (mods unload)'
if (Get-Process windhawk -ErrorAction SilentlyContinue) { & $wh -exit -wait; Start-Sleep -Seconds 4 }

'== native values'
New-ItemProperty -Path $adv -Name TaskbarLocation -Value 0 -PropertyType DWord -Force | Out-Null   # ABE_LEFT
New-ItemProperty -Path $adv -Name TaskbarSize -Value 0 -PropertyType DWord -Force | Out-Null       # Small
'TaskbarLocation=0 TaskbarSize=0'

'== restart explorer'
Restart-Explorer

'== measure'
& $probe -Label native 2>$null | Where-Object { $_ -notlike 'screenshot:*' }
& $uia

'== native bar left on screen; run native-test.ps1 -Restore to come back to Windhawk'
