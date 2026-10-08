# List UTF-16 strings that look like taskbar registry value names in the shell binaries.
$core = Join-Path $env:WINDIR 'SystemApps\MicrosoftWindows.Client.Core_cw5n1h2txyewy'
$files = @((Join-Path $env:WINDIR 'System32\taskbar.dll'), (Join-Path $core 'Taskbar.View.dll'), (Join-Path $core 'SystemTray.dll'), (Join-Path $env:WINDIR 'explorer.exe'))
foreach ($p in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    $hits = [regex]::Matches($text, '(?<![A-Za-z0-9_])(Taskbar(?:Location|Sz|Si|Size|Position|Edge|SmallIcons|IconSize|ButtonSize|Mode|Al|Glom|Da|Mn|Sd|Sn|Sh)[A-Za-z0-9_]*)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    "== $(Split-Path $p -Leaf): $($hits.Count) names"
    $hits -join ', '
}
