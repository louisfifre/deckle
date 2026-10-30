# Install manifest: what an installation owns and where it came from.
#
# The manifest lives in the installed copy only, under scripts/.deckle-scripts/.
# The update reads it to tell a file it installed and left alone from one that
# was edited locally since. Deleting it only cuts updates: the menu never reads it.

function Sort-OrdinalStrings {
    param([AllowEmptyCollection()][string[]]$Value = @())

    # Sort-Object compares by culture; the manifest must come out identical on
    # every machine, so the order is by code point.
    $copy = [string[]]@($Value)
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    return $copy
}

function Get-ContentFingerprint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    # CRLF is folded to LF first: a checkout that changes line endings must not
    # look like a local edit. Latin-1 maps bytes to characters one to one, so
    # the fold is safe on any content and never alters the other bytes.
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::Latin1.GetString($bytes).Replace("`r`n", "`n")
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::Latin1.GetBytes($text))
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-InstallManifestPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    return Join-Path $RepositoryRoot 'scripts/.deckle-scripts/manifest.json'
}

function Read-InstallManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $path = Get-InstallManifestPath -RepositoryRoot $RepositoryRoot
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    } catch {
        throw "The install manifest '$path' is not valid JSON ($($_.Exception.Message)). Delete the folder scripts/.deckle-scripts to reinstall."
    }
}

function Get-InstallManifestFiles {
    [CmdletBinding()]
    param([AllowNull()]$Manifest)

    # ConvertFrom-Json turns the files map into an object; callers want a lookup.
    $files = [ordered]@{}
    if ($null -eq $Manifest -or $null -eq $Manifest.files) { return $files }
    if ($Manifest.files -is [System.Collections.IDictionary]) {
        foreach ($key in $Manifest.files.Keys) { $files[[string]$key] = [string]$Manifest.files[$key] }
    } else {
        foreach ($property in $Manifest.files.PSObject.Properties) {
            $files[[string]$property.Name] = [string]$property.Value
        }
    }
    return $files
}

function Write-InstallManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)]$Manifest
    )

    $sortedFiles = [ordered]@{}
    $files = Get-InstallManifestFiles -Manifest $Manifest
    foreach ($relative in (Sort-OrdinalStrings -Value @($files.Keys))) {
        $sortedFiles[$relative] = $files[$relative]
    }

    # A manifest read back carries installedAt as a DateTime; it is written as
    # the same ISO 8601 UTC text either way.
    $installedAt = $Manifest.installedAt
    if ($installedAt -is [datetime]) { $installedAt = $installedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }

    $document = [ordered]@{
        source      = [string]$Manifest.source
        version     = [string]$Manifest.version
        packs       = [string[]]@(Sort-OrdinalStrings -Value @($Manifest.packs))
        installedAt = [string]$installedAt
        files       = $sortedFiles
    }

    # LF and no byte order mark: the same bytes on every checkout.
    $json = ($document | ConvertTo-Json -Depth 4).Replace("`r`n", "`n") + "`n"
    $path = Get-InstallManifestPath -RepositoryRoot $RepositoryRoot
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force
    [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
}
