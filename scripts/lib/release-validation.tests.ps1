$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'release-validation.psm1') -Force

function Assert-Throws([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") {
            throw "Expected error containing '$Expected', got '$($_.Exception.Message)'"
        }
        return
    }
    throw "Expected action to throw: $Expected"
}

function Invoke-Git([string]$Root, [string[]]$Arguments) {
    & git -C $Root @Arguments *> $null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-release-validation-$([guid]::NewGuid())"
$remote = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-release-validation-remote-$([guid]::NewGuid()).git"
try {
    $null = New-Item -ItemType Directory -Path $root
    & git init --bare $remote *> $null
    if ($LASTEXITCODE -ne 0) { throw 'bare git init failed' }
    Invoke-Git $root @('init', '-b', 'main')
    Invoke-Git $root @('config', 'user.email', 'tests@deckle.local')
    Invoke-Git $root @('config', 'user.name', 'Deckle Tests')
    Set-Content -LiteralPath (Join-Path $root 'tracked.txt') -Value 'release source'
    Invoke-Git $root @('add', 'tracked.txt')
    Invoke-Git $root @('commit', '-m', 'feat: release source')
    Invoke-Git $root @('remote', 'add', 'origin', $remote)
    Invoke-Git $root @('push', '-u', 'origin', 'main')

    $firstHead = (& git -C $root rev-parse HEAD).Trim()
    Publish-DeckleReleaseTag -RepoRoot $root -Tag 'v0.13.8' -HeadSha $firstHead
    Publish-DeckleReleaseTag -RepoRoot $root -Tag 'v0.13.8' -HeadSha $firstHead
    $publishedTag = (& git --git-dir=$remote rev-parse 'v0.13.8^{commit}').Trim()
    if ($publishedTag -cne $firstHead) { throw 'Release tag was not published at release HEAD' }

    Set-Content -LiteralPath (Join-Path $root 'tracked.txt') -Value 'next release source'
    Invoke-Git $root @('add', 'tracked.txt')
    Invoke-Git $root @('commit', '-m', 'fix: advance release source')
    Invoke-Git $root @('push', 'origin', 'main')
    $secondHead = (& git -C $root rev-parse HEAD).Trim()
    Invoke-Git $root @('tag', 'v0.13.9', $firstHead)
    Invoke-Git $root @('push', 'origin', 'refs/tags/v0.13.9')
    Invoke-Git $root @('tag', '-d', 'v0.13.9')
    Assert-Throws {
        Publish-DeckleReleaseTag -RepoRoot $root -Tag 'v0.13.9' -HeadSha $secondHead
    } 'not release HEAD'

    $expectedAssets = [ordered]@{
        'Deckle-Setup-v0.13.10-win-x64.exe' = 100
        'Deckle-v0.13.10.zip'               = 200
        'Deckle-v0.13.10.zip.sha256'        = 64
    }
    $draft = [pscustomobject]@{
        isDraft         = $true
        tagName         = 'v0.13.10'
        targetCommitish = $secondHead
        assets          = @(
            [pscustomobject]@{ name = 'Deckle-Setup-v0.13.10-win-x64.exe'; size = 100 }
            [pscustomobject]@{ name = 'Deckle-v0.13.10.zip'; size = 200 }
            [pscustomobject]@{ name = 'Deckle-v0.13.10.zip.sha256'; size = 64 }
        )
    }
    Assert-DeckleReleaseDraft -Release $draft -Tag 'v0.13.10' -HeadSha $secondHead -ExpectedAssets $expectedAssets

    $publishedRelease = $draft.PSObject.Copy()
    $publishedRelease.isDraft = $false
    Assert-Throws {
        Assert-DeckleReleaseDraft -Release $publishedRelease -Tag 'v0.13.10' -HeadSha $secondHead -ExpectedAssets $expectedAssets
    } 'not a resumable draft'

    Assert-Throws {
        Assert-DeckleReleaseDraft -Release $draft -Tag 'v0.13.10' -HeadSha $firstHead -ExpectedAssets $expectedAssets
    } 'does not target release HEAD'

    $wrongAssets = [ordered]@{} + $expectedAssets
    $wrongAssets['Deckle-v0.13.10.zip'] = 201
    Assert-Throws {
        Assert-DeckleReleaseDraft -Release $draft -Tag 'v0.13.10' -HeadSha $secondHead -ExpectedAssets $wrongAssets
    } 'missing or has the wrong size'

    # The source validator intentionally requires a GitHub remote. Rewrite only
    # the configured URL after the local push; no network operation follows.
    Invoke-Git $root @('remote', 'set-url', 'origin', 'https://github.com/louisfifre/deckle.git')

    $ready = Assert-DeckleReleaseSource -RepoRoot $root -Version '0.13.10' -LatestPublishedTag 'v0.13.7'
    if ($ready.OwnerRepo -cne 'louisfifre/deckle') { throw 'GitHub owner/repo was not resolved' }

    Set-Content -LiteralPath (Join-Path $root 'untracked.cs') -Value 'class Surprise {}'
    Assert-Throws {
        Assert-DeckleReleaseSource -RepoRoot $root -Version '0.13.10' -LatestPublishedTag 'v0.13.7'
    } 'not clean'
    Remove-Item -LiteralPath (Join-Path $root 'untracked.cs')

    Assert-Throws {
        Assert-DeckleReleaseSource -RepoRoot $root -Version '0.13.7' -LatestPublishedTag 'v0.13.7'
    } 'must be newer'

    Invoke-Git $root @('switch', '-c', 'fix/not-main')
    Assert-Throws {
        Assert-DeckleReleaseSource -RepoRoot $root -Version '0.13.10' -LatestPublishedTag 'v0.13.7'
    } 'must be cut from main'

    $publish = Join-Path $root 'publish'
    $null = New-Item -ItemType Directory -Path $publish
    Set-Content -LiteralPath (Join-Path $publish 'Deckle.exe') -Value 'exe'
    Set-Content -LiteralPath (Join-Path $publish 'Deckle.pri') -Value 'pri'
    $zip = Join-Path $root 'Deckle-v0.13.8.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
    Assert-DeckleReleaseArchive -PublishDir $publish -ZipPath $zip

    $brokenZip = Join-Path $root 'broken.zip'
    $stream = [System.IO.File]::Open($brokenZip, [System.IO.FileMode]::Create)
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream, [System.IO.Compression.ZipArchiveMode]::Create)
    $null = $archive.CreateEntry('../Deckle.exe')
    $null = $archive.CreateEntry('Deckle.pri')
    $archive.Dispose()
    $stream.Dispose()
    Assert-Throws {
        Assert-DeckleReleaseArchive -PublishDir $publish -ZipPath $brokenZip
    } 'unsafe path'

    Write-Host 'release-validation.tests.ps1 passed' -ForegroundColor Green
} finally {
    if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    if (Test-Path $remote) { Remove-Item -LiteralPath $remote -Recurse -Force }
}
