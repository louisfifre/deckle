# Compiles a Windhawk mod source outside the Windhawk UI, with the command the
# UI extension itself runs (compileModInternal in UI\resources\app\extensions\
# windhawk\dist\extension.js, Windhawk 1.7.3). Checked on 2026-10-17: the
# official taskbar-icon-size 1.3.10 source compiled this way gives a DLL of the
# same size as the one Windhawk compiled.
#
# Usage: pwsh -File windhawk-build-mod.ps1 -Source <mod.wh.cpp> -Out <mod.dll>
#        [-WindhawkRoot D:\bin\scoop\apps\windhawk\current]
#
# The mod id, version and @compilerOptions are read from the source header.
# Only the x86-64 target is handled: that is the one explorer.exe needs here.
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Out,
    [string] $WindhawkRoot = 'D:\bin\scoop\apps\windhawk\current'
)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $WindhawkRoot 'Compiler'
$clang = Join-Path $compiler 'bin\clang++.exe'
if (-not (Test-Path -LiteralPath $clang)) { throw "clang++ not found at $clang" }

# The engine import library sits under Engine\<engine version>\64.
$engineLib = Get-ChildItem -LiteralPath (Join-Path $WindhawkRoot 'Engine') -Directory |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName '64\windhawk.lib' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $engineLib) { throw "windhawk.lib not found under $WindhawkRoot\Engine" }

# Mod metadata from the ==WindhawkMod== header.
$header = Get-Content -LiteralPath $Source -TotalCount 60
function Read-HeaderField([string] $name) {
    $line = $header | Where-Object { $_ -match "^//\s*@$name\s+(.+?)\s*$" } | Select-Object -First 1
    if ($line -and $line -match "^//\s*@$name\s+(.+?)\s*$") { return $Matches[1] }
    return $null
}
$modId = Read-HeaderField 'id'
$modVersion = Read-HeaderField 'version'
if (-not $modId -or -not $modVersion) { throw 'The source header lacks @id or @version' }
$modOptions = @()
$opt = Read-HeaderField 'compilerOptions'
if ($opt) { $modOptions = $opt -split '\s+' }

$clangArgs = @(
    '-std=c++23', '-O2', '-shared', '-DUNICODE', '-D_UNICODE',
    '-DWINVER=0x0A00', '-D_WIN32_WINNT=0x0A00', '-D_WIN32_IE=0x0A00', '-DNTDDI_VERSION=0x0A000008',
    '-D__USE_MINGW_ANSI_STDIO=0', '-DWH_MOD',
    ('-DWH_MOD_ID=L"' + $modId + '"'),
    ('-DWH_MOD_VERSION=L"' + $modVersion + '"'),
    $engineLib,
    '-x', 'c++', $Source,
    '-include', 'windhawk_api.h',
    '-target', 'x86_64-w64-mingw32',
    '-Wl,--export-all-symbols',
    '-o', $Out
) + $modOptions

New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
Push-Location $compiler
try {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $clang @clangArgs 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $sw.Stop()
    "mod=$modId version=$modVersion exit=$code seconds=$([int]$sw.Elapsed.TotalSeconds)"
    if (Test-Path -LiteralPath $Out) { "dll bytes=$((Get-Item -LiteralPath $Out).Length)" }
}
finally {
    Pop-Location
}
exit $code
