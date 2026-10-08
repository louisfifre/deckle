# Native taskbar test, to run once the feature flags are enabled in the Runtime store.
# Sets the native position/size values, piggybacks the tray and clock probes, restarts explorer, measures, then restores the probes.
$ErrorActionPreference = 'Continue'
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$intl = 'HKCU:\Control Panel\International'
$tray = 'HKCU:\Control Panel\NotifyIconSettings'
$probe = Join-Path $PSScriptRoot 'taskbar-probe.ps1'
$uia = Join-Path $PSScriptRoot 'tray-uia.ps1'
$flags = Join-Path $PSScriptRoot 'feature-flags.ps1'

'== flags'
& $flags -Query -Ids 59213768,61161244,61754985,62353331,62762248

'== arm the probes'
$origTime = (Get-ItemProperty $intl).sShortTime
$origDate = (Get-ItemProperty $intl).sShortDate
$ptKey = Join-Path $tray '36923934268172301'   # Deckle.exe tray entry (running), IsPromoted=0 before
$origPt = (Get-ItemProperty $ptKey).IsPromoted
Set-ItemProperty $intl -Name sShortTime -Value 'HH mm'
Set-ItemProperty $intl -Name sShortDate -Value 'ddd'
Set-ItemProperty $ptKey -Name IsPromoted -Value 1 -Type DWord
New-ItemProperty -Path $adv -Name TaskbarLocation -Value 0 -PropertyType DWord -Force | Out-Null   # ABE_LEFT
New-ItemProperty -Path $adv -Name TaskbarSize -Value 0 -PropertyType DWord -Force | Out-Null       # Small
"TaskbarLocation=0 TaskbarSize=0 sShortTime='HH mm' sShortDate='ddd' PowerToys IsPromoted=1"

'== restart explorer'
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 5
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process (Join-Path $env:WINDIR 'explorer.exe') }
Start-Sleep -Seconds 8

'== measure'
& $probe -Label native 2>$null | Where-Object { $_ -notlike 'screenshot:*' }
& $uia

'== restore the probes (position and size are left for Louis to see)'
Set-ItemProperty $intl -Name sShortTime -Value $origTime
Set-ItemProperty $intl -Name sShortDate -Value $origDate
if ($null -eq $origPt) { Remove-ItemProperty -Path $ptKey -Name IsPromoted -ErrorAction SilentlyContinue } else { Set-ItemProperty $ptKey -Name IsPromoted -Value ([int]$origPt) -Type DWord }
"restored sShortTime='$origTime' sShortDate='$origDate' PowerToys IsPromoted=$origPt"
