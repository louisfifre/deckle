# Repository version read from git, the one source of versions.
#
# A version is an annotated tag `vX.Y.Z` on a commit. Nothing falls back: a
# commit without such a tag has no version, and the caller says what to do.
# Every reader of the version goes through these functions, so a later
# change of source control touches one file.

function ConvertTo-SemanticVersion {
    param([Parameter(Mandatory)][string]$Text)

    # [version] sorts numerically where a string sort would put v0.10 before v0.9.
    $value = $Text.TrimStart('v')
    if ($value -notmatch '^\d+\.\d+\.\d+$') { return $null }
    return [version]$value
}

function Get-RepositoryVersionTags {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $tags = @(& git -C $Path tag --list 'v[0-9]*' 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git could not list the tags of '$Path'." }
    $versions = foreach ($tag in $tags) {
        $version = ConvertTo-SemanticVersion -Text $tag
        if ($null -ne $version) { [pscustomobject]@{ Tag = $tag; Version = $version } }
    }
    return @($versions | Sort-Object -Property Version)
}

function Get-RepositoryVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Commit = 'HEAD'
    )

    $tags = @(& git -C $Path tag --points-at $Commit --list 'v[0-9]*' 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git could not read the tags of '$Path' at $Commit." }
    $versions = @(@(foreach ($tag in $tags) {
        $version = ConvertTo-SemanticVersion -Text $tag
        if ($null -ne $version) { $version }
    }) | Sort-Object)
    if ($versions.Count -eq 0) {
        $short = (& git -C $Path rev-parse --short $Commit 2>$null)
        throw "Commit $short of '$Path' carries no version tag. Record one with an annotated tag vX.Y.Z (git/record-version.ps1) before building or installing from it."
    }
    return [string]$versions[-1]
}

function Get-LatestRepositoryVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $tags = @(Get-RepositoryVersionTags -Path $Path)
    if ($tags.Count -eq 0) { return $null }
    return [string]$tags[-1].Version
}
