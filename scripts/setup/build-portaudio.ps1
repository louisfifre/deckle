<#
.SYNOPSIS
    Build PortAudio
.DESCRIPTION
    Rebuilds the portaudio.dll that Deckle.Audio ships, from the pinned
    PortAudio commit, with the WASAPI host API only, and writes it with its
    license and provenance into src/Deckle.Audio/Native/win-x64. Needs git,
    CMake and Visual Studio with the C++ workload. The result is committed:
    run this only to move to another PortAudio commit or change an option.
.NOTES
    order: 140
    action: PortAudio / build |
#>

[CmdletBinding()]
param(
    # Working folder for the clone and the build tree. Defaults to a folder
    # under the system temp directory, removed at the end.
    [string]$WorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The pinned upstream commit. PortAudio's last release, v19.7.0, dates from
# 2021; master carries five years of WASAPI fixes since. The commit is pinned
# rather than followed because PaWasapiStreamInfo changed size after v19.7.0
# (56 bytes there, 72 here): the C# mirror in PortAudioInterop must match the
# DLL it calls, byte for byte, or every open fails.
$Repository = 'https://github.com/PortAudio/portaudio.git'
$Commit     = '1ca0eeedb678c3cca80f3306c474d69a7a4b5278'

# WASAPI only. ASIO stays out until its license is settled (a product
# decision); WMME, DirectSound and WDM-KS are host APIs Deckle does not use.
# The static C runtime keeps the DLL free of the Visual C++ Redistributable.
# PA_WASAPI_MAX_CONST_DEVICE_COUNT enables PaWasapi_UpdateDeviceList, the one
# way to see a microphone plugged in after Pa_Initialize without terminating.
$CMakeOptions = @(
    '-DPA_USE_WASAPI=ON'
    '-DPA_USE_ASIO=OFF'
    '-DPA_USE_WMME=OFF'
    '-DPA_USE_DS=OFF'
    '-DPA_USE_WDMKS=OFF'
    '-DPA_USE_WDMKS_DEVICE_INFO=OFF'
    '-DPA_BUILD_SHARED_LIBS=ON'
    '-DPA_DLL_LINK_WITH_STATIC_RUNTIME=ON'
    '-DPA_BUILD_TESTS=OFF'
    '-DPA_BUILD_EXAMPLES=OFF'
    '-DCMAKE_C_FLAGS=/DPA_WASAPI_MAX_CONST_DEVICE_COUNT=32'
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$target   = Join-Path $repoRoot 'src\Deckle.Audio\Native\win-x64'

$ownsWorkDir = -not $WorkDir
if ($ownsWorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) "deckle-portaudio-$([guid]::NewGuid().ToString('N').Substring(0, 8))" }
$source = Join-Path $WorkDir 'portaudio'
$build  = Join-Path $WorkDir 'build'

try {
    New-Item -ItemType Directory -Force $WorkDir | Out-Null

    Write-Host "Fetching PortAudio $Commit"
    git init --quiet $source
    git -C $source fetch --quiet --depth 1 $Repository $Commit
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed ($LASTEXITCODE)" }
    git -C $source checkout --quiet FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw "git checkout failed ($LASTEXITCODE)" }

    Write-Host 'Configuring'
    cmake -S $source -B $build -A x64 @CMakeOptions
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }

    Write-Host 'Building Release'
    cmake --build $build --config Release
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($LASTEXITCODE)" }

    $dll = Get-ChildItem $build -Recurse -Filter 'portaudio.dll' | Select-Object -First 1
    if (-not $dll) { throw 'portaudio.dll not found in the build tree' }

    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $dll.FullName (Join-Path $target 'portaudio.dll') -Force
    Copy-Item (Join-Path $source 'LICENSE.txt') (Join-Path $target 'portaudio.LICENSE.txt') -Force

    $hash = (Get-FileHash (Join-Path $target 'portaudio.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    $provenance = @(
        '# portaudio.dll'
        ''
        'Built by `scripts/setup/build-portaudio.ps1`; rerun it rather than editing this folder by hand.'
        ''
        "- Source: $Repository"
        "- Commit: $Commit"
        "- Options: $($CMakeOptions -join ' ')"
        "- SHA256: $hash"
        ''
        'The license PortAudio asks to keep with every copy is `portaudio.LICENSE.txt`, beside the DLL.'
    )
    Set-Content -Path (Join-Path $target 'README.md') -Value $provenance -Encoding utf8NoBOM

    Write-Host "portaudio.dll written to $target"
    Write-Host "SHA256 $hash"
}
finally {
    if ($ownsWorkDir -and (Test-Path $WorkDir)) { Remove-Item -Recurse -Force $WorkDir }
}
