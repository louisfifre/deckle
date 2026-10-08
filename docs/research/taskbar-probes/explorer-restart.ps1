# Restart explorer.exe cleanly (the shell relaunches itself when killed; start it again if it does not) and measure the taskbar.
$ErrorActionPreference = 'Continue'
$before = Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id
"explorer before: $($before -join ',')"
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process (Join-Path $env:WINDIR 'explorer.exe'); Start-Sleep -Seconds 4 }
$after = Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id
"explorer after: $($after -join ',')"
Start-Sleep -Seconds 3
& (Join-Path $PSScriptRoot 'taskbar-probe.ps1') -Label ($args[0] ?? 'after-restart') 2>$null | Where-Object { $_ -notlike 'screenshot:*' }
