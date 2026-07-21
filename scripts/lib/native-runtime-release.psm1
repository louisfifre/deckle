# Reads and validates the native runtime artifact pinned by the app. App
# publication uses this module as a fail-closed preflight so a Deckle installer
# cannot be released while its first-run dependency is absent or mismatched.

$ErrorActionPreference = 'Stop'

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

Export-ModuleMember -Function Get-DeckleNativeRuntimeBundle, Assert-DeckleNativeRuntimeArtifact
