$ErrorActionPreference = 'Stop'

function Get-ReleaseHistoryPath {
    param([Parameter(Mandatory)][string]$RepoRoot)
    return Join-Path $RepoRoot 'release-history.json'
}

function Get-PublishedReleaseTags {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $path = Get-ReleaseHistoryPath -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Release history not found at $path"
    }

    try {
        $document = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    } catch {
        throw "Release history is not valid JSON: $path"
    }

    $tags = @($document.PublishedTags)
    if (-not $tags.Count) { throw "Release history contains no published tags: $path" }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $previous = $null
    foreach ($tag in $tags) {
        if ($tag -cnotmatch '^v\d+\.\d+\.\d+$') {
            throw "Invalid published release tag '$tag' in $path"
        }
        if (-not $seen.Add($tag)) { throw "Duplicate published release tag '$tag' in $path" }

        $version = [version]$tag.Substring(1)
        if ($null -ne $previous -and $version -le $previous) {
            throw "Published release tags are not strictly ascending in $path"
        }
        $previous = $version
    }

    return [string[]]$tags
}

function Add-PublishedReleaseTag {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Tag
    )

    if ($Tag -cnotmatch '^v\d+\.\d+\.\d+$') {
        throw "Invalid published release tag '$Tag'"
    }

    $tags = @(Get-PublishedReleaseTags -RepoRoot $RepoRoot)
    if ($tags -contains $Tag) { return $false }

    $latest = [version]$tags[-1].Substring(1)
    $next = [version]$Tag.Substring(1)
    if ($next -le $latest) {
        throw "Published release $Tag must be newer than $($tags[-1])"
    }

    $tags += $Tag
    $document = [ordered]@{ PublishedTags = $tags }
    $json = $document | ConvertTo-Json -Depth 2
    $path = Get-ReleaseHistoryPath -RepoRoot $RepoRoot
    [System.IO.File]::WriteAllText($path, $json + "`n", [System.Text.UTF8Encoding]::new($false))
    return $true
}

Export-ModuleMember -Function Get-ReleaseHistoryPath, Get-PublishedReleaseTags, Add-PublishedReleaseTag
