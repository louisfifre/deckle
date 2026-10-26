$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force
. (Join-Path $PSScriptRoot 'pe-fixture.ps1')

function Assert-Throws([scriptblock]$Action, [string]$MessageFragment) {
    try {
        & $Action
        throw "Expected failure containing '$MessageFragment'"
    } catch {
        if ($_.Exception.Message -notlike "*$MessageFragment*") { throw }
    }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-native-release-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $root
    $artifact = Join-Path $root 'deckle-native-1.9.1.zip'
    [IO.File]::WriteAllBytes($artifact, [byte[]](1, 2, 3, 4))
    $sha256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $source = Join-Path $root 'NativeRuntime.cs'
    @"
public static NativeRuntimeBundle CurrentBundle { get; } = new(
    Version:     "1.9.1",
    Url:         "https://example.test/deckle-native-1.9.1.zip",
    Sha256:      "$sha256",
    SizeBytes:   4L,
    DisplayName: "Whisper.cpp + Vulkan runtime");
"@ | Set-Content -LiteralPath $source

    $bundle = Get-DeckleNativeRuntimeBundle -SourcePath $source
    if ($bundle.Version -cne '1.9.1' -or $bundle.SizeBytes -ne 4) {
        throw 'Native runtime metadata was not parsed correctly'
    }
    $verified = Assert-DeckleNativeRuntimeArtifact -Bundle $bundle -ArtifactPath $artifact
    if ($verified.Sha256 -cne $sha256) { throw 'Native runtime hash was not returned' }

    $whisperRepo = Join-Path $root 'whisper.cpp'
    $null = New-Item -ItemType Directory -Path $whisperRepo
    # Upstream's current spelling, with the project name quoted.
    Set-Content -LiteralPath (Join-Path $whisperRepo 'CMakeLists.txt') -Value 'project("whisper.cpp" VERSION 1.9.4)'
    $sameSeriesPlan = Get-DeckleNativeRuntimeVersionPlan `
        -SourcePath $source `
        -WhisperRepo $whisperRepo `
        -PublishedTags @('native-v1.0.0', 'native-v1.9.2')
    if ($sameSeriesPlan.Version -cne '1.9.3' -or $sameSeriesPlan.WhisperVersion -cne '1.9.4') {
        throw 'The next native runtime should increment the latest local rebuild counter'
    }

    Set-Content -LiteralPath (Join-Path $whisperRepo 'CMakeLists.txt') -Value 'project(whisper VERSION 1.10.0)'
    $newSeriesPlan = Get-DeckleNativeRuntimeVersionPlan -SourcePath $source -WhisperRepo $whisperRepo
    if ($newSeriesPlan.Version -cne '1.10.0' -or -not $newSeriesPlan.SeriesChanged) {
        throw 'A new whisper.cpp minor series should start at rebuild counter zero'
    }

    Set-Content -LiteralPath (Join-Path $whisperRepo 'CMakeLists.txt') -Value 'project(whisper VERSION 1.8.9)'
    Assert-Throws {
        Get-DeckleNativeRuntimeVersionPlan -SourcePath $source -WhisperRepo $whisperRepo
    } 'older than the latest native runtime series'

    $wrongSize = $bundle.PSObject.Copy()
    $wrongSize.SizeBytes = 5
    Assert-Throws { Assert-DeckleNativeRuntimeArtifact -Bundle $wrongSize -ArtifactPath $artifact } 'size mismatch'

    $wrongHash = $bundle.PSObject.Copy()
    $wrongHash.Sha256 = '00' * 32
    Assert-Throws { Assert-DeckleNativeRuntimeArtifact -Bundle $wrongHash -ArtifactPath $artifact } 'SHA-256 mismatch'

    $catalogSource = Join-Path $root 'NativeRuntimeCatalog.cs'
    @'
public const string EntryDll = "whisper.dll";
public static IReadOnlyList<string> RequiredDllNames { get; } = new[]
{
    "vcruntime140.dll",
    "vcomp140.dll",
    "ggml-base.dll",
    "ggml.dll",
    "ggml-vulkan.dll",
    EntryDll,
};
'@ | Set-Content -LiteralPath $catalogSource
    $catalog = Get-DeckleNativeRuntimeCatalog -SourcePath $catalogSource
    if (($catalog.Names -join ',') -cne 'vcruntime140.dll,vcomp140.dll,ggml-base.dll,ggml.dll,ggml-vulkan.dll,whisper.dll') {
        throw 'Native runtime catalog order or values were not read from the C# authority'
    }
    if (($catalog.WhisperDlls -join ',') -cne 'ggml-base.dll,ggml.dll,ggml-vulkan.dll,whisper.dll' -or
        ($catalog.RuntimeDlls -join ',') -cne 'vcruntime140.dll,vcomp140.dll') {
        throw 'Native runtime catalog source groups were not classified correctly'
    }

    # A build tree names its compiler; the runtime DLLs come from the Redist
    # folders of that same Visual Studio, found from the compiler path.
    $vsRoot = Join-Path $root 'vs'
    $clDir = Join-Path $vsRoot 'VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64'
    $crtDir = Join-Path $vsRoot 'VC\Redist\MSVC\14.51.36231\x64\Microsoft.VC145.CRT'
    $ompDir = Join-Path $vsRoot 'VC\Redist\MSVC\14.51.36231\x64\Microsoft.VC145.OpenMP'
    $olderCrtDir = Join-Path $vsRoot 'VC\Redist\MSVC\14.40.33807\x64\Microsoft.VC143.CRT'
    $buildBin = Join-Path $whisperRepo 'build-msvc\bin'
    $null = New-Item -ItemType Directory -Path $clDir, $crtDir, $ompDir, $olderCrtDir, $buildBin
    $clExe = Join-Path $clDir 'cl.exe'
    Set-Content -LiteralPath $clExe -Value 'fixture compiler'
    New-DeckleTestPeImage -Path (Join-Path $crtDir 'vcruntime140.dll') -Imports @('KERNEL32.dll')
    New-DeckleTestPeImage -Path (Join-Path $olderCrtDir 'vcruntime140.dll') -Imports @('KERNEL32.dll')
    New-DeckleTestPeImage -Path (Join-Path $ompDir 'vcomp140.dll') -Imports @('KERNEL32.dll')
    New-DeckleTestPeImage -Path (Join-Path $buildBin 'ggml-base.dll') -Imports @('vcruntime140.dll', 'vcomp140.dll', 'KERNEL32.dll')
    New-DeckleTestPeImage -Path (Join-Path $buildBin 'ggml.dll') -Imports @('ggml-base.dll', 'vcruntime140.dll')
    New-DeckleTestPeImage -Path (Join-Path $buildBin 'ggml-vulkan.dll') -Imports @('ggml-base.dll', 'vulkan-1.dll')
    New-DeckleTestPeImage -Path (Join-Path $buildBin 'whisper.dll') -Imports @('ggml.dll', 'ggml-base.dll', 'vcruntime140.dll')
    @(
        "CMAKE_CXX_COMPILER:STRING=$($clExe -replace '\\', '/')"
        'GGML_BACKEND_DL:BOOL=ON'
        'GGML_VULKAN:BOOL=ON'
    ) | Set-Content -LiteralPath (Join-Path $whisperRepo 'build-msvc\CMakeCache.txt')
    $build = Get-DeckleNativeRuntimeBuild -WhisperRepo $whisperRepo -BuildDir 'build-msvc'
    if ($build.RedistVersion -cne '14.51.36231') { throw 'The newest Redist beside the compiler should be chosen' }
    if ($build.RedistDirs.Count -ne 2) { throw 'The CRT and OpenMP Redist folders should both be found' }
    if (($build.BuildFlags -join ',') -cne 'GGML_BACKEND_DL:BOOL=ON,GGML_VULKAN:BOOL=ON') { throw 'Build flags were not read from the CMake cache' }
    $sources = Resolve-DeckleNativeRuntimeDllSources -Build $build -Catalog $catalog
    if ($sources['vcruntime140.dll'] -cne (Join-Path $crtDir 'vcruntime140.dll')) { throw 'vcruntime140.dll should come from the CRT Redist folder' }
    if ($sources['vcomp140.dll'] -cne (Join-Path $ompDir 'vcomp140.dll')) { throw 'vcomp140.dll should come from the OpenMP Redist folder' }
    if ($sources['whisper.dll'] -cne (Join-Path $buildBin 'whisper.dll')) { throw 'whisper.dll should come from the build tree' }
    Assert-Throws { Get-DeckleNativeRuntimeBuild -WhisperRepo $whisperRepo -BuildDir 'build' } 'build output not found'

    # A MinGW build is refused: its runtime is not the Visual C++ Redist.
    $mingwBin = Join-Path $whisperRepo 'build-mingw\bin'
    $null = New-Item -ItemType Directory -Path $mingwBin
    $gxx = Join-Path $root 'mingw\bin\g++.exe'
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $gxx)
    Set-Content -LiteralPath $gxx -Value 'fixture compiler'
    "CMAKE_CXX_COMPILER:STRING=$($gxx -replace '\\', '/')" | Set-Content -LiteralPath (Join-Path $whisperRepo 'build-mingw\CMakeCache.txt')
    Assert-Throws { Get-DeckleNativeRuntimeBuild -WhisperRepo $whisperRepo -BuildDir 'build-mingw' } 'built with MSVC (cl.exe)'

    $staging = Join-Path $root 'staging'
    $null = New-Item -ItemType Directory -Path $staging
    foreach ($name in $catalog.Names) { Copy-Item -LiteralPath $sources[$name] -Destination (Join-Path $staging $name) }
    Set-Content -LiteralPath (Join-Path $staging 'PROVENANCE.txt') -Value 'fixture provenance'
    $sumLines = foreach ($name in $catalog.Names) { "$('0' * 64) *$name" }
    Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS') -Value $sumLines
    $invalidArchive = Join-Path $root 'native-invalid.zip'
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $invalidArchive
    Assert-Throws {
        Assert-DeckleNativeRuntimeArchive -ArchivePath $invalidArchive -DllNames $catalog.Names
    } 'SHA256SUMS mismatch'

    $sumLines = foreach ($name in $catalog.Names) {
        $dllPath = Join-Path $staging $name
        "$((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()) *$name"
    }
    Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS') -Value $sumLines
    $archive = Join-Path $root 'native.zip'
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive
    Assert-DeckleNativeRuntimeArchive -ArchivePath $archive -DllNames $catalog.Names

    # vulkan-1.dll is the declared external import; everything else in the
    # archive must resolve inside it.
    Assert-DeckleNativeRuntimeArchiveLoadable -ArchivePath $archive -DllNames $catalog.Names

    # The bug that reached the first outside tester: a DLL importing the
    # compiler's own runtime, which the build machine finds on PATH and a
    # bare machine does not. The archive is refused by name of the import.
    New-DeckleTestPeImage -Path (Join-Path $staging 'ggml.dll') -Imports @('ggml-base.dll', 'libgomp-1.dll')
    $sumLines = foreach ($name in $catalog.Names) {
        $dllPath = Join-Path $staging $name
        "$((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLowerInvariant()) *$name"
    }
    Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS') -Value $sumLines
    $leakyArchive = Join-Path $root 'native-leaky.zip'
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $leakyArchive
    Assert-DeckleNativeRuntimeArchive -ArchivePath $leakyArchive -DllNames $catalog.Names
    Assert-Throws {
        Assert-DeckleNativeRuntimeArchiveLoadable -ArchivePath $leakyArchive -DllNames $catalog.Names
    } 'ggml.dll imports libgomp-1.dll, which is neither in the bundle nor part of Windows'

    Write-Host 'native-runtime-release.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
