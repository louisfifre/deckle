# Reads and validates the native runtime artifact pinned by the app. App
# publication uses this module as a fail-closed preflight so a Deckle installer
# cannot be released while the bundle its setup wizard downloads is absent or
# mismatched.

$ErrorActionPreference = 'Stop'
# No -Force here: forcing a nested import unloads the copy a calling script
# imported for itself.
Import-Module (Join-Path $PSScriptRoot 'pe-imports.psm1')

function Get-DeckleNativeRuntimeCatalog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Native runtime source not found: $SourcePath"
    }

    $source = Get-Content -LiteralPath $SourcePath -Raw
    $entryMatch = [regex]::Match($source, 'const\s+string\s+EntryDll\s*=\s*"(?<value>[^"]+)"')
    if (-not $entryMatch.Success) {
        throw "Native runtime EntryDll declaration not found in $SourcePath"
    }

    $catalogMatch = [regex]::Match(
        $source,
        'RequiredDllNames\s*\{\s*get;\s*\}\s*=\s*new\[\]\s*\{(?<body>.*?)\};',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $catalogMatch.Success) {
        throw "Native runtime RequiredDllNames declaration not found in $SourcePath"
    }

    $names = [System.Collections.Generic.List[string]]::new()
    $tokenPattern = 'EntryDll|"(?<value>[^"]+)"'
    foreach ($token in [regex]::Matches($catalogMatch.Groups['body'].Value, $tokenPattern)) {
        $name = if ($token.Value -ceq 'EntryDll') {
            $entryMatch.Groups['value'].Value
        } else {
            $token.Groups['value'].Value
        }
        if (-not $names.Contains($name)) { $names.Add($name) }
    }
    if (-not $names.Count) {
        throw "Native runtime RequiredDllNames is empty in $SourcePath"
    }

    # whisper.cpp's own output (the entry DLL and the ggml family) comes from
    # the build tree; everything else is the Visual C++ runtime, taken from
    # the Redist folder of the toolset that produced the build.
    $whisperDlls = @($names | Where-Object { $_ -ceq $entryMatch.Groups['value'].Value -or $_ -like 'ggml*.dll' })
    $runtimeDlls = @($names | Where-Object { $_ -notin $whisperDlls })
    if (-not $whisperDlls.Count -or -not $runtimeDlls.Count) {
        throw "Native runtime catalog cannot be separated into whisper.cpp and Visual C++ runtime files"
    }

    return [pscustomobject]@{
        EntryDll    = $entryMatch.Groups['value'].Value
        Names       = @($names)
        WhisperDlls = $whisperDlls
        RuntimeDlls = $runtimeDlls
    }
}

# The one import the bundle leaves to the machine: the Vulkan loader comes
# with the graphics driver, and ggml-vulkan.dll is a backend the app loads
# only when that loader is there.
function Get-DeckleNativeExternalDllNames {
    [CmdletBinding()]
    param()
    return @('vulkan-1.dll')
}

function Get-DeckleNativeRuntimeBuild {
    <#
    .SYNOPSIS
        Locates a whisper.cpp build tree and the Visual C++ Redist folders
        paired with the compiler that produced it.
    .DESCRIPTION
        The bundle ships the runtime DLLs the build imports, so they must come
        from the same toolset: the Redist folder is read from the cl.exe path
        CMake recorded, never from PATH or from System32.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WhisperRepo,
        [string]$BuildDir = 'build'
    )

    $buildRoot = Join-Path $WhisperRepo $BuildDir
    $bin = Join-Path $buildRoot 'bin'
    if (-not (Test-Path -LiteralPath $bin -PathType Container)) {
        throw "whisper.cpp build output not found: $bin (cmake --build needed first)"
    }
    $cmakeCache = Join-Path $buildRoot 'CMakeCache.txt'
    if (-not (Test-Path -LiteralPath $cmakeCache -PathType Leaf)) {
        throw "whisper.cpp CMake cache not found: $cmakeCache"
    }

    $compilerMatch = Select-String -LiteralPath $cmakeCache `
        -Pattern '^CMAKE_CXX_COMPILER:(?:FILEPATH|STRING)=(.+)$' |
        Select-Object -First 1
    if (-not $compilerMatch) { throw "CMAKE_CXX_COMPILER not found in $cmakeCache" }
    $compiler = $compilerMatch.Matches[0].Groups[1].Value.Trim()
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw "C++ compiler recorded by whisper.cpp no longer exists: $compiler"
    }
    if ((Split-Path -Leaf $compiler) -ine 'cl.exe') {
        throw "whisper.cpp was built with $compiler; the native runtime is built with MSVC (cl.exe) so that its runtime is the Visual C++ Redistributable"
    }

    # cl.exe sits at <VC>\Tools\MSVC\<tools>\bin\Host<arch>\<arch>\cl.exe; the
    # Redist of the same Visual Studio is <VC>\Redist\MSVC\<redist>\x64\.
    $vcRoot = $compiler
    for ($i = 0; $i -lt 7; $i++) { $vcRoot = Split-Path -Parent $vcRoot }
    $redistRoot = Join-Path $vcRoot 'Redist\MSVC'
    $redistVersions = @(Get-ChildItem -LiteralPath $redistRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending)
    if (-not $redistVersions.Count) {
        throw "Visual C++ Redist folder not found beside the compiler: $redistRoot"
    }
    $redist = $redistVersions[0]
    $redistDirs = @(Get-ChildItem -LiteralPath (Join-Path $redist.FullName 'x64') -Directory |
        Where-Object { $_.Name -like 'Microsoft.VC*.CRT' -or $_.Name -like 'Microsoft.VC*.OpenMP' } |
        ForEach-Object FullName)
    if (-not $redistDirs.Count) {
        throw "Visual C++ Redist $($redist.Name) has no x64 CRT folder"
    }

    $flags = @(Select-String -LiteralPath $cmakeCache -Pattern '^(GGML_VULKAN|GGML_BACKEND_DL|GGML_CPU_ALL_VARIANTS|GGML_NATIVE|GGML_OPENMP|BUILD_SHARED_LIBS):BOOL=' |
        ForEach-Object Line)

    return [pscustomobject]@{
        Bin           = $bin
        CMakeCache    = $cmakeCache
        Compiler      = $compiler
        RedistVersion = $redist.Name
        RedistDirs    = $redistDirs
        BuildFlags    = $flags
    }
}

function Resolve-DeckleNativeRuntimeDllSources {
    <#
    .SYNOPSIS
        Maps every catalog DLL to the file it is copied from: the build's
        bin folder for whisper.cpp output, the Redist folders for the runtime.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Build,
        [Parameter(Mandatory)][psobject]$Catalog
    )

    $sources = [ordered]@{}
    foreach ($name in $Catalog.WhisperDlls) {
        $path = Join-Path $Build.Bin $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "whisper.cpp build is missing $name under $($Build.Bin); the catalog and the CMake options disagree"
        }
        $sources[$name] = $path
    }
    foreach ($name in $Catalog.RuntimeDlls) {
        $found = $null
        foreach ($dir in $Build.RedistDirs) {
            $candidate = Join-Path $dir $name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $found = $candidate; break }
        }
        if (-not $found) {
            throw "Visual C++ Redist $($Build.RedistVersion) does not ship $name under $($Build.RedistDirs -join ', ')"
        }
        $sources[$name] = $found
    }
    return $sources
}

function Get-DeckleNativeRuntimeBundle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Native runtime source not found: $SourcePath"
    }

    $source = Get-Content -LiteralPath $SourcePath -Raw
    $block = [regex]::Match(
        $source,
        'CurrentBundle\s*\{\s*get;\s*\}\s*=\s*new\s*\((?<body>.*?)\);',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $block.Success) {
        throw "CurrentBundle declaration not found in $SourcePath"
    }

    $body = $block.Groups['body'].Value
    function Read-StringField([string]$name) {
        $pattern = '{0}:\s*"(?<value>[^"]+)"' -f [regex]::Escape($name)
        $match = [regex]::Match($body, $pattern)
        if (-not $match.Success) { throw "CurrentBundle.$name not found in $SourcePath" }
        return $match.Groups['value'].Value
    }

    $sizeMatch = [regex]::Match($body, 'SizeBytes:\s*(?<value>[\d_]+)L')
    if (-not $sizeMatch.Success) { throw "CurrentBundle.SizeBytes not found in $SourcePath" }

    return [pscustomobject]@{
        Version   = Read-StringField 'Version'
        Url       = Read-StringField 'Url'
        Sha256    = Read-StringField 'Sha256'
        SizeBytes = [long]($sizeMatch.Groups['value'].Value.Replace('_', ''))
    }
}

function Get-DeckleNativeRuntimeVersionPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$WhisperRepo,
        [string[]]$PublishedTags = @()
    )

    $cmakeLists = Join-Path $WhisperRepo 'CMakeLists.txt'
    if (-not (Test-Path -LiteralPath $cmakeLists -PathType Leaf)) {
        throw "whisper.cpp CMakeLists.txt not found: $cmakeLists"
    }

    $cmakeSource = Get-Content -LiteralPath $cmakeLists -Raw
    # Upstream has written both project(whisper VERSION x) and
    # project("whisper.cpp" VERSION x) over time.
    $upstreamMatch = [regex]::Match(
        $cmakeSource,
        'project\s*\(\s*"?whisper(?:\.cpp)?"?\b[^)]*\bVERSION\s+(?<value>\d+\.\d+\.\d+)',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $upstreamMatch.Success) {
        throw "whisper.cpp project version not found in $cmakeLists"
    }

    $whisperVersionText = $upstreamMatch.Groups['value'].Value
    $whisperVersion = [version]$whisperVersionText
    $bundle = Get-DeckleNativeRuntimeBundle -SourcePath $SourcePath
    if ([string]$bundle.Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Current native runtime version '$($bundle.Version)' is not canonical X.Y.Z"
    }

    $knownVersions = [System.Collections.Generic.List[version]]::new()
    $knownVersions.Add([version]$bundle.Version)
    foreach ($tag in @($PublishedTags)) {
        if ([string]$tag -match '^native-v(?<value>\d+\.\d+\.\d+)$') {
            $knownVersions.Add([version]$Matches.value)
        }
    }

    $highestKnown = @($knownVersions | Sort-Object -Descending | Select-Object -First 1)[0]
    $upstreamSeries = [version]::new($whisperVersion.Major, $whisperVersion.Minor)
    $highestKnownSeries = [version]::new($highestKnown.Major, $highestKnown.Minor)
    if ($upstreamSeries -lt $highestKnownSeries) {
        throw "whisper.cpp $whisperVersionText is older than the latest native runtime series $($highestKnown.Major).$($highestKnown.Minor).x"
    }

    $sameSeries = @($knownVersions | Where-Object {
        $_.Major -eq $whisperVersion.Major -and $_.Minor -eq $whisperVersion.Minor
    })
    $nextCounter = if ($sameSeries.Count -eq 0) {
        0
    } else {
        [int](@($sameSeries | Measure-Object -Property Build -Maximum)[0].Maximum) + 1
    }

    return [pscustomobject]@{
        Version         = "$($whisperVersion.Major).$($whisperVersion.Minor).$nextCounter"
        WhisperVersion  = $whisperVersionText
        PreviousVersion = $highestKnown.ToString(3)
        SeriesChanged   = $upstreamSeries -gt $highestKnownSeries
    }
}

function Assert-DeckleNativeRuntimeArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Bundle,
        [Parameter(Mandatory)][string]$ArtifactPath
    )

    if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
        throw "Native runtime download is missing: $ArtifactPath"
    }

    $actualSize = (Get-Item -LiteralPath $ArtifactPath).Length
    if ($actualSize -ne [long]$Bundle.SizeBytes) {
        throw "Native runtime size mismatch: expected $($Bundle.SizeBytes), got $actualSize"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $ArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -cne ([string]$Bundle.Sha256).ToLowerInvariant()) {
        throw "Native runtime SHA-256 mismatch: expected $($Bundle.Sha256), got $actualSha256"
    }

    return [pscustomobject]@{
        Version   = $Bundle.Version
        Url       = $Bundle.Url
        SizeBytes = $actualSize
        Sha256    = $actualSha256
    }
}

function Assert-DeckleNativeRuntimeArchive {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string[]]$DllNames
    )

    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
        throw "Native runtime archive is missing: $ArchivePath"
    }

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $expected = @($DllNames) + @('PROVENANCE.txt', 'SHA256SUMS')
        $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
        if ($entries.Count -ne $expected.Count) {
            throw "Native runtime archive contains missing or unexpected files"
        }
        foreach ($entry in $entries) {
            if ($entry.FullName -match '[/\\]' -or $entry.FullName -match '(^|/)\.\.(/|$)') {
                throw "Native runtime archive is not flat: $($entry.FullName)"
            }
            if ($entry.Length -le 0) { throw "Native runtime archive file is empty: $($entry.FullName)" }
        }
        foreach ($name in $expected) {
            if (@($entries | Where-Object FullName -CEQ $name).Count -ne 1) {
                throw "Native runtime archive is missing $name"
            }
        }

        $sumsEntry = $entries | Where-Object FullName -CEQ 'SHA256SUMS' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($sumsEntry.Open())
        try { $sumLines = @($reader.ReadToEnd() -split "\r?\n" | Where-Object { $_ }) }
        finally { $reader.Dispose() }
        if ($sumLines.Count -ne $DllNames.Count) {
            throw 'Native runtime SHA256SUMS does not cover every DLL'
        }
        foreach ($name in $DllNames) {
            $line = @($sumLines | Where-Object { $_ -match "^(?<hash>[0-9a-fA-F]{64}) \*$([regex]::Escape($name))$" })
            if ($line.Count -ne 1) { throw "Native runtime SHA256SUMS is missing $name" }
            $expectedHash = [regex]::Match($line[0], '^(?<hash>[0-9a-fA-F]{64})').Groups['hash'].Value.ToLowerInvariant()
            $dllEntry = $entries | Where-Object FullName -CEQ $name | Select-Object -First 1
            $stream = $dllEntry.Open()
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $actualHash = [Convert]::ToHexString($sha256.ComputeHash($stream)).ToLowerInvariant()
            } finally {
                $sha256.Dispose()
                $stream.Dispose()
            }
            if ($actualHash -cne $expectedHash) {
                throw "Native runtime SHA256SUMS mismatch for $name"
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-DeckleNativeRuntimeArchiveLoadable {
    <#
    .SYNOPSIS
        Extracts the archive's DLLs to a scratch folder and refuses any import
        a bare Windows machine would not resolve from the bundle itself.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string[]]$DllNames
    )

    $scratch = Join-Path ([IO.Path]::GetTempPath()) "deckle-native-imports-$([guid]::NewGuid())"
    try {
        $null = New-Item -ItemType Directory -Path $scratch
        Add-Type -AssemblyName System.IO.Compression
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -notlike '*.dll' -or $entry.FullName -match '[/\\]') { continue }
                $stream = $entry.Open()
                try {
                    $file = [IO.File]::Create((Join-Path $scratch $entry.FullName))
                    try { $stream.CopyTo($file) } finally { $file.Dispose() }
                } finally { $stream.Dispose() }
            }
        } finally {
            $archive.Dispose()
        }
        Assert-DeckleNativeImportsResolved `
            -Directory $scratch `
            -DllNames $DllNames `
            -ExternalDllNames (Get-DeckleNativeExternalDllNames)
    } finally {
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    }
}

Export-ModuleMember -Function Get-DeckleNativeRuntimeCatalog, Get-DeckleNativeExternalDllNames, Get-DeckleNativeRuntimeBuild, Resolve-DeckleNativeRuntimeDllSources, Get-DeckleNativeRuntimeBundle, Get-DeckleNativeRuntimeVersionPlan, Assert-DeckleNativeRuntimeArtifact, Assert-DeckleNativeRuntimeArchive, Assert-DeckleNativeRuntimeArchiveLoadable
