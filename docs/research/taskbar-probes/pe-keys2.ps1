# Check whether the Windhawk symbol-cache key is TimeDateStamp-SizeOfImage (not file size).
$core = Join-Path $env:WINDIR 'SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy'
$files = @((Join-Path $env:WINDIR 'System32\taskbar.dll'), (Join-Path $core 'Taskbar.View.dll'), (Join-Path $core 'SystemTray.dll'))
$w = 'D:\bin\scoop\persist\windhawk\AppData\Engine\ModsWritable'
$cache = (Get-ChildItem (Join-Path $w '*.ini') | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join "`n"
foreach ($p in $files) {
    $b = [System.IO.File]::ReadAllBytes($p)
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    $ts = [BitConverter]::ToUInt32($b, $pe + 8)
    $sizeOfImage = [BitConverter]::ToUInt32($b, $pe + 24 + 56)
    $key = "$ts-$sizeOfImage"
    $hits = ([regex]::Matches($cache, [regex]::Escape("#$key#"))).Count
    "{0,-18} key={1,-22} cache hits across mods={2}" -f (Split-Path $p -Leaf), $key, $hits
}
