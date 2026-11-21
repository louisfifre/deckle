# Reads the SHA-256 the C# code pins for each downloaded model and checks a
# file against one, so a setup script never trusts a download on its size
# alone. The hashes stay in SpeechModels.cs and SileroVadModel.cs, which the
# app's setup wizard also checks; nothing here keeps a copy of them.

$ErrorActionPreference = 'Stop'

function Get-DeckleSpeechModelHashes {
    <#
    .SYNOPSIS
        Maps each Whisper model file name to the SHA-256 SpeechModels.cs pins.
    .DESCRIPTION
        A ModelEntry names its file through a constant for the default model,
        so the file name is read from the end of its Url, which every entry
        spells out.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Speech models source not found: $SourcePath"
    }
    $source = Get-Content -LiteralPath $SourcePath -Raw
    $entries = [regex]::Matches(
        $source,
        'new\s+ModelEntry\((?<body>.*?)\)\s*[,;]',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)

    $hashes = [ordered]@{}
    foreach ($entry in $entries) {
        $body = $entry.Groups['body'].Value
        $url = [regex]::Match($body, 'Url:\s*"(?<value>[^"]+)"')
        $sha = [regex]::Match($body, 'Sha256:\s*"(?<value>[0-9a-fA-F]{64})"')
        if (-not $url.Success -or -not $sha.Success) {
            throw "A ModelEntry in $SourcePath has no readable Url and Sha256"
        }
        $hashes[[uri]::new($url.Groups['value'].Value).Segments[-1]] = $sha.Groups['value'].Value.ToLowerInvariant()
    }
    if (-not $hashes.Count) {
        throw "No ModelEntry found in $SourcePath"
    }
    return $hashes
}

function Get-DeckleSileroVadHash {
    <#
    .SYNOPSIS
        The SHA-256 SileroVadModel.cs pins for the Silero VAD model.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Silero VAD source not found: $SourcePath"
    }
    $match = [regex]::Match(
        (Get-Content -LiteralPath $SourcePath -Raw),
        'const\s+string\s+Sha256\s*=\s*"(?<value>[0-9a-fA-F]{64})"')
    if (-not $match.Success) {
        throw "SileroVadModel.Sha256 not found in $SourcePath"
    }
    return $match.Groups['value'].Value.ToLowerInvariant()
}

function Test-DeckleFileSha256 {
    <#
    .SYNOPSIS
        True when the file exists and its SHA-256 equals the expected one.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    return $actual -ceq $ExpectedSha256.ToLowerInvariant()
}

Export-ModuleMember -Function Get-DeckleSpeechModelHashes, Get-DeckleSileroVadHash, Test-DeckleFileSha256
