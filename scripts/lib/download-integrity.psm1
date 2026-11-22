# Reads the SHA-256 the C# code pins for each downloaded model and checks a
# file against one, so a setup script never trusts a download on its size
# alone. The hashes stay in ParakeetModelCatalog.cs and SileroVadModel.cs,
# which the app's setup wizard also checks; nothing here keeps a copy of them.

$ErrorActionPreference = 'Stop'

function Get-DeckleParakeetModelFiles {
    <#
    .SYNOPSIS
        The files of the Parakeet model ParakeetModelCatalog.cs pins: path,
        size, SHA-256 and download URL.
    .DESCRIPTION
        Reads the default file list (the FP32 set the wizard downloads), not the
        optional FP16 encoder. The URL is built from the catalog's repository
        and revision, the way ParakeetModelFile.Url is.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Parakeet model catalog source not found: $SourcePath"
    }
    $source = Get-Content -LiteralPath $SourcePath -Raw
    $repository = [regex]::Match($source, 'const\s+string\s+Repository\s*=\s*"(?<value>[^"]+)"')
    $revision = [regex]::Match($source, 'const\s+string\s+Revision\s*=\s*"(?<value>[0-9a-f]{40})"')
    if (-not $repository.Success -or -not $revision.Success) {
        throw "Repository and Revision not found in $SourcePath"
    }
    $list = [regex]::Match(
        $source,
        'Files\s*\{\s*get;\s*\}\s*=\s*\[(?<body>.*?)\];',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $list.Success) {
        throw "ParakeetModelCatalog.Files not found in $SourcePath"
    }

    $files = foreach ($entry in [regex]::Matches(
            $list.Groups['body'].Value,
            'new\("(?<path>[^"]+)",\s*(?<size>[0-9_]+),\s*"(?<sha>[0-9a-fA-F]{64})"\)')) {
        [pscustomobject]@{
            Path      = $entry.Groups['path'].Value
            SizeBytes = [long]($entry.Groups['size'].Value -replace '_', '')
            Sha256    = $entry.Groups['sha'].Value.ToLowerInvariant()
            Url       = "https://huggingface.co/$($repository.Groups['value'].Value)/resolve/$($revision.Groups['value'].Value)/$($entry.Groups['path'].Value)"
        }
    }
    if (-not @($files).Count) {
        throw "No file found in ParakeetModelCatalog.Files of $SourcePath"
    }
    return @($files)
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

Export-ModuleMember -Function Get-DeckleParakeetModelFiles, Get-DeckleSileroVadHash, Test-DeckleFileSha256
