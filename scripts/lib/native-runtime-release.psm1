# Reads and validates the native runtime artifact pinned by the app. App
# publication uses this module as a fail-closed preflight so a Deckle installer
# cannot be released while its first-run dependency is absent or mismatched.

$ErrorActionPreference = 'Stop'

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

    $whisperDlls = @($names | Where-Object { $_ -ceq $entryMatch.Groups['value'].Value -or $_ -like 'ggml*.dll' })
    $mingwDlls = @($names | Where-Object { $_ -notin $whisperDlls })
    if (-not $whisperDlls.Count -or -not $mingwDlls.Count) {
        throw "Native runtime catalog cannot be separated into whisper.cpp and MinGW files"
    }

    return [pscustomobject]@{
        EntryDll    = $entryMatch.Groups['value'].Value
        Names       = @($names)
        WhisperDlls = $whisperDlls
        MingwDlls   = $mingwDlls
    }
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
    $upstreamMatch = [regex]::Match(
        $cmakeSource,
        'project\s*\(\s*whisper\b[^)]*\bVERSION\s+(?<value>\d+\.\d+\.\d+)',
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

Export-ModuleMember -Function Get-DeckleNativeRuntimeCatalog, Get-DeckleNativeRuntimeBundle, Get-DeckleNativeRuntimeVersionPlan, Assert-DeckleNativeRuntimeArtifact, Assert-DeckleNativeRuntimeArchive
