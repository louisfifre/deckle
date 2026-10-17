# Installs a compiled mod into portable Windhawk by its data files, the way the
# UI does after a compile: the DLL goes to Engine\Mods\64, the source replaces
# the one in ModsSource, the mod's INI points at the new DLL and its
# SettingsChangeTime moves so the running engine reloads the mod. Checked on
# 2026-10-17: explorer swapped taskbar-icon-size to the new DLL within seconds,
# no Windhawk restart.
#
# Usage: pwsh -File windhawk-install-mod.ps1 -Dll <mod.dll> -Source <mod.wh.cpp>
#        [-Set TaskbarHeight=44 -Set IconSize=16] [-AppData <Windhawk AppData>]
#        [-BackupDir <dir>]
#        pwsh -File windhawk-install-mod.ps1 -Source <mod.wh.cpp> -Restore
#
# -Restore puts back the INI and the source saved by the first install of that
# mod. The DLL copied in stays, the INI no longer names it.
param(
    [string] $Dll,
    [Parameter(Mandatory)] [string] $Source,
    [string[]] $Set = @(),
    [string] $AppData = 'D:\bin\scoop\persist\windhawk\AppData',
    [string] $BackupDir = (Join-Path $env:LOCALAPPDATA 'Deckle\windhawk-backup'),
    [switch] $Restore
)

$ErrorActionPreference = 'Stop'

$header = Get-Content -LiteralPath $Source -TotalCount 60
$idLine = $header | Where-Object { $_ -match '^//\s*@id\s+(.+?)\s*$' } | Select-Object -First 1
if (-not ($idLine -and $idLine -match '^//\s*@id\s+(.+?)\s*$')) { throw 'The source header lacks @id' }
$modId = $Matches[1]

$iniPath = Join-Path $AppData "Engine\Mods\$modId.ini"
$srcPath = Join-Path $AppData "ModsSource\$modId.wh.cpp"
$modsDir = Join-Path $AppData 'Engine\Mods\64'
$iniBackup = Join-Path $BackupDir "$modId.ini"
$srcBackup = Join-Path $BackupDir "$modId.wh.cpp"

if ($Restore) {
    Copy-Item -LiteralPath $srcBackup -Destination $srcPath -Force
    [IO.File]::WriteAllBytes($iniPath, [IO.File]::ReadAllBytes($iniBackup))
    "restored $modId INI and source from $BackupDir"
    exit 0
}

if (-not $Dll) { throw 'Give -Dll, or -Restore' }
if (-not (Test-Path -LiteralPath $iniPath)) { throw "$modId is not installed: no $iniPath" }

New-Item -ItemType Directory -Force $BackupDir | Out-Null
if (-not (Test-Path -LiteralPath $iniBackup)) { Copy-Item -LiteralPath $iniPath -Destination $iniBackup }
if (-not (Test-Path -LiteralPath $srcBackup)) { Copy-Item -LiteralPath $srcPath -Destination $srcBackup }

# Windhawk writes its INI files in UTF-16LE with a BOM; keep whatever is there.
$raw = [IO.File]::ReadAllBytes($iniPath)
$utf16 = $raw.Length -ge 2 -and $raw[0] -eq 0xFF -and $raw[1] -eq 0xFE
$enc = if ($utf16) { [Text.Encoding]::Unicode } else { [Text.UTF8Encoding]::new($false) }
$text = $enc.GetString($raw)
if ($utf16 -and $text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }

$dllName = Split-Path -Leaf $Dll
Copy-Item -LiteralPath $Dll -Destination (Join-Path $modsDir $dllName) -Force
Copy-Item -LiteralPath $Source -Destination $srcPath -Force

$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$text = [regex]::Replace($text, '(?m)^LibraryFileName=.*$', "LibraryFileName=$dllName")
$text = [regex]::Replace($text, '(?m)^SettingsChangeTime=.*$', "SettingsChangeTime=$now")
foreach ($pair in $Set) {
    $k, $v = $pair -split '=', 2
    if (-not $v) { throw "-Set expects key=value, got '$pair'" }
    $pattern = "(?m)^$([regex]::Escape($k))=.*$"
    if ($text -notmatch $pattern) { throw "setting $k is not in $iniPath" }
    $text = [regex]::Replace($text, $pattern, "$k=$v")
}

$outBytes = $enc.GetBytes($text)
if ($utf16) { $outBytes = [byte[]](0xFF, 0xFE) + $outBytes }
[IO.File]::WriteAllBytes($iniPath, $outBytes)

"installed $modId as $dllName, backups in $BackupDir"
$text
