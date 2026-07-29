# Release publication invariants shared by publish-app.ps1 and its tests.
# Every assertion fails closed: a release is cheaper to postpone than repair on
# machines that may already have discovered it.

$ErrorActionPreference = 'Stop'

function Invoke-ReleaseGit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    # Git may emit ambient warnings from the maintainer's global excludes file;
    # they are not command output and must not make a clean tree look dirty.
    $output = @(& git -C $RepoRoot @Arguments 2>$null)
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        throw "git $($Arguments -join ' ') failed (code $code)"
    }
    return $output
}

function ConvertTo-ReleaseVersion {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Label
    )

    $bare = $Value.TrimStart('v')
    $parsed = $null
    if (-not [version]::TryParse($bare, [ref]$parsed) -or
        $parsed.Build -lt 0 -or $parsed.Revision -ge 0 -or
        $bare -cne $parsed.ToString(3)) {
        throw "$Label '$Value' is not canonical MAJOR.MINOR.PATCH"
    }
    return $parsed
}

function Assert-DeckleReleaseSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$LatestPublishedTag
    )

    $candidate = ConvertTo-ReleaseVersion -Value $Version -Label 'Version'
    $latest = ConvertTo-ReleaseVersion -Value $LatestPublishedTag -Label 'Latest published tag'
    if ($candidate -le $latest) {
        throw "Version v$Version must be newer than $LatestPublishedTag"
    }

    $source = Assert-DeckleReleaseRepositorySource -RepoRoot $RepoRoot
    $headSha = $source.HeadSha

    $tag = "v$Version"
    $tagSha = @(& git -C $RepoRoot rev-parse --verify --quiet "$tag^{commit}" 2>$null)
    $tagCode = $LASTEXITCODE
    if ($tagCode -eq 0 -and $tagSha[0].Trim() -cne $headSha) {
        throw "Existing tag $tag points to $($tagSha[0].Trim()), not release HEAD $headSha"
    }
    if ($tagCode -ne 0 -and $tagCode -ne 1) {
        throw "git rev-parse $tag failed (code $tagCode)"
    }

    return [pscustomobject]@{
        HeadSha   = $headSha
        OwnerRepo = $source.OwnerRepo
        Tag       = $tag
    }
}

function Assert-DeckleReleaseRepositorySource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [switch]$AllowAhead
    )

    $dirty = @(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @(
        'status', '--porcelain=v1', '--untracked-files=all'))
    if ($dirty.Count) {
        throw "Release source is not clean; commit or remove every tracked and untracked change:`n$($dirty -join "`n")"
    }

    $branch = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @('branch', '--show-current')))[0].Trim()
    if ($branch -cne 'main') {
        throw "Releases must be cut from main, not '$branch'"
    }

    $upstream = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @(
        'rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}')))[0].Trim()
    if ($upstream -cne 'origin/main') {
        throw "main must track origin/main before release (found '$upstream')"
    }

    $headSha = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @('rev-parse', 'HEAD')))[0].Trim()
    $upstreamSha = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @('rev-parse', '@{upstream}')))[0].Trim()
    if ($headSha -cne $upstreamSha) {
        if (-not $AllowAhead) {
            throw "HEAD $headSha is not synchronized with origin/main $upstreamSha"
        }
        & git -C $RepoRoot merge-base --is-ancestor $upstreamSha $headSha
        if ($LASTEXITCODE -ne 0) {
            throw "HEAD $headSha has diverged from origin/main $upstreamSha"
        }
    }

    $remoteUrl = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @(
        'remote', 'get-url', 'origin')))[0].Trim()
    if ($remoteUrl -notmatch 'github\.com[:/](?<repo>[^/]+/[^/.]+)(?:\.git)?$') {
        throw "origin is not a GitHub repository: $remoteUrl"
    }
    $ownerRepo = $Matches.repo

    return [pscustomobject]@{
        HeadSha   = $headSha
        OwnerRepo = $ownerRepo
    }
}

function Get-DeckleReleaseRecoveryPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][bool]$Recorded,
        [AllowNull()][psobject]$Release
    )

    if ($null -eq $Release) {
        if ($Recorded) { return 'Inconsistent' }
        return 'Build'
    }
    if ($Release.isDraft) { return 'ResumeDraft' }
    if ($Recorded) { return 'Complete' }
    return 'RecordPublic'
}

function Assert-DeckleReleaseAssets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Release,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter(Mandatory)][string[]]$ExpectedNames,
        [System.Collections.IDictionary]$ExpectedSizes,
        [switch]$RequireDraft
    )

    if ($RequireDraft -and -not $Release.isDraft) {
        throw "GitHub release $Tag already exists and is not a resumable draft"
    }
    if ($Release.tagName -cne $Tag -or $Release.targetCommitish -cne $HeadSha) {
        throw "GitHub release $Tag does not target release HEAD $HeadSha"
    }
    if (@($Release.assets).Count -ne $ExpectedNames.Count) {
        throw "GitHub release $Tag contains missing or unexpected assets"
    }
    foreach ($name in $ExpectedNames) {
        $matches = @($Release.assets | Where-Object { $_.name -ceq $name })
        if ($matches.Count -ne 1 -or [long]$matches[0].size -le 0) {
            throw "GitHub release asset $name is missing or empty"
        }
        if ($ExpectedSizes -and $ExpectedSizes.Contains($name) -and
            [long]$matches[0].size -ne [long]$ExpectedSizes[$name]) {
            throw "GitHub release asset $name has the wrong size"
        }
    }
}

function Get-ReleaseRemoteTagCommit {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Tag
    )

    $tagRef = "refs/tags/$Tag"
    $peeledRef = "$tagRef^{}"
    $refs = @(& git -C $RepoRoot ls-remote origin $tagRef $peeledRef 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-remote $Tag failed (code $LASTEXITCODE)"
    }
    if (-not $refs.Count) { return $null }

    $selected = $refs | Where-Object { ($_ -split '\s+', 2)[1] -ceq $peeledRef } | Select-Object -First 1
    if (-not $selected) {
        $selected = $refs | Where-Object { ($_ -split '\s+', 2)[1] -ceq $tagRef } | Select-Object -First 1
    }
    if (-not $selected) { throw "Remote tag $Tag returned an unreadable ref" }
    return ($selected -split '\s+', 2)[0]
}

function Publish-DeckleReleaseTag {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$HeadSha
    )

    $resolvedHead = (@(Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @(
        'rev-parse', "$HeadSha^{commit}")))[0].Trim()
    if ($resolvedHead -cne $HeadSha) {
        throw "Release HEAD $HeadSha resolves to $resolvedHead"
    }

    $remoteTagSha = Get-ReleaseRemoteTagCommit -RepoRoot $RepoRoot -Tag $Tag
    if ($remoteTagSha -and $remoteTagSha -cne $HeadSha) {
        throw "Remote tag $Tag points to $remoteTagSha, not release HEAD $HeadSha"
    }

    $localTag = @(& git -C $RepoRoot rev-parse --verify --quiet "$Tag^{commit}" 2>$null)
    $localTagCode = $LASTEXITCODE
    if ($localTagCode -eq 0) {
        if ($localTag[0].Trim() -cne $HeadSha) {
            throw "Local tag $Tag points to $($localTag[0].Trim()), not release HEAD $HeadSha"
        }
    } elseif ($localTagCode -eq 1) {
        $null = Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @('tag', $Tag, $HeadSha)
    } else {
        throw "git rev-parse $Tag failed (code $localTagCode)"
    }

    if (-not $remoteTagSha) {
        $refspec = "refs/tags/{0}:refs/tags/{0}" -f $Tag
        $null = Invoke-ReleaseGit -RepoRoot $RepoRoot -Arguments @('push', 'origin', $refspec)
    }

    $publishedTagSha = Get-ReleaseRemoteTagCommit -RepoRoot $RepoRoot -Tag $Tag
    if ($publishedTagSha -cne $HeadSha) {
        throw "Remote tag $Tag does not point to release HEAD $HeadSha"
    }
}

function Assert-DeckleReleaseDraft {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Release,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter(Mandatory)][System.Collections.IDictionary]$ExpectedAssets
    )

    Assert-DeckleReleaseAssets `
        -Release $Release `
        -Tag $Tag `
        -HeadSha $HeadSha `
        -ExpectedNames @($ExpectedAssets.Keys) `
        -ExpectedSizes $ExpectedAssets `
        -RequireDraft
}

function Assert-DeckleReleaseArchive {
    [CmdletBinding()]
    param(
        [string]$PublishDir,
        [Parameter(Mandatory)][string]$ZipPath
    )

    if (-not (Test-Path -LiteralPath $ZipPath -PathType Leaf)) {
        throw "Release archive is missing: $ZipPath"
    }

    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $names = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $fileCount = 0
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (-not $name.EndsWith('/')) { $fileCount++ }
            if ($name.StartsWith('/') -or $name -match '(^|/)\.\.(/|$)') {
                throw "Release archive contains unsafe path '$name'"
            }
            if (-not $names.Add($name)) {
                throw "Release archive contains duplicate path '$name'"
            }
        }

        foreach ($required in @('Deckle.exe', 'Deckle.pri')) {
            if (-not $names.Contains($required)) {
                throw "Release archive is missing required root entry $required"
            }
        }

        if ($PublishDir) {
            $publishedFileCount = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File).Count
            if ($fileCount -ne $publishedFileCount) {
                throw "Release archive contains $fileCount files; publish folder contains $publishedFileCount"
            }
        }
    } finally {
        $archive.Dispose()
    }
}

Export-ModuleMember -Function Assert-DeckleReleaseRepositorySource, Assert-DeckleReleaseSource, Get-DeckleReleaseRecoveryPlan, Assert-DeckleReleaseAssets, Assert-DeckleReleaseArchive, Assert-DeckleReleaseDraft, Publish-DeckleReleaseTag
