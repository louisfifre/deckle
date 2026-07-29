# publish-app.ps1
#
# Cuts a Deckle GitHub Release: builds the two artefacts a release carries and
# (with -Publish) uploads them under the version tag.
#
#   1. The installer stub — Deckle-Setup-v<X.Y.Z>-win-x64.exe — the HEADLINE
#      asset, the file an end user downloads and runs. A small NativeAOT exe
#      (src/Deckle.Installer) that resolves the latest release, pulls the app ZIP
#      below, verifies its sha256, extracts it per-user, and registers Deckle.
#   2. The self-contained app payload — Deckle-v<X.Y.Z>.zip + .sha256 — NOT a
#      user-facing download but the soute the installer fetches: the whole
#      publish folder (Deckle.exe + .NET runtime + Windows App SDK + Deckle.pri +
#      assets), run in place, xcopy deployment (folder, NOT single-file:
#      rationale in the _IsPublishing PropertyGroup of Deckle.App.csproj).
#
# GitHub auto-attaches the source-code archives — nothing to do for those.
#
# What it does NOT ship: the whisper.cpp native runtime and the Whisper models.
# Those are provisioned per-user by the app's first-run wizard (the native
# bundle has its own publish-native-runtime.ps1 / native-vX.Y.Z release cycle).
#
# `publish` is the maintainer's act: without -Publish this builds both artefacts
# locally for inspection; -Publish ALSO creates the GitHub Release via gh.
#
# Pendant to publish-native-runtime.ps1 — same Step/Ok/Warn idiom, same
# zip + sha256 + paste-ready-summary + optional -Publish shape.

[CmdletBinding()]
param(
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,

    # Interactive picker: lists the main repo + all linked worktrees and
    # prompts for a choice. Overrides -Target.
    [switch]$Pick,

    # Output directory for the publish folder + produced ZIP. Defaults to a
    # versioned subfolder under <RepoRoot>\artifacts\ (git-ignored). Never the
    # system temp dir — build artefacts stay on the repo's volume, off C:.
    [string]$OutDir,

    # Also publish the installer exe + the app ZIP (+ its .sha256 sidecar) as a
    # GitHub Release tagged v$Version via gh. Requires gh authenticated against
    # the repo's remote. The version is read from the csproj; gh attaches to the
    # commit only after every artifact has built successfully.
    [switch]$Publish,

    # Optional release-notes file passed to `gh release create --notes-file`.
    # Without it, the notes are generated from the commit history by
    # changelog.ps1 (home-grown, plain git log, no GitHub API).
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/lib/
. (Join-Path $ScriptDir 'action-summary.ps1')
. (Join-Path $ScriptDir 'deckle-process.ps1')
Import-Module (Join-Path $ScriptDir 'release-history.psm1') -Force
Import-Module (Join-Path $ScriptDir 'release-validation.psm1') -Force
Import-Module (Join-Path $ScriptDir 'native-runtime-release.psm1') -Force

function Step($msg) { Write-Host "`n[publish] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "           $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "           $msg" -ForegroundColor Yellow }

$Workflow = if ($Publish) { 'Publish app release' } else { 'Prepare app release artifacts' }
$RepoRoot = $null
$Version = $null
$SetupPath = $null
$ZipPath = $null
$ZipSha256 = $null
$Published = $false

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "$Workflow failed before completion." `
        -Details ([ordered]@{
            Worktree  = $RepoRoot
            Version   = $(if ($Version) { "v$Version" } else { $null })
            OutDir    = $OutDir
            Installer = $SetupPath
            Payload   = $ZipPath
            Published = $Published
            Error     = $_.Exception.Message
        })
    throw
}

# ── RepoRoot resolution (mirrors build-run.ps1) ──────────────────────────────
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}

Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

$ProjectDir = Join-Path $RepoRoot 'src\Deckle.App'
$Csproj     = Join-Path $ProjectDir 'Deckle.App.csproj'
if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle repo?" }

# ── Read <Version> — single source of truth is the csproj ────────────────────
$Version  = $null
$verMatch = Select-String -Path $Csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if ($verMatch) { $Version = $verMatch.Matches[0].Groups[1].Value.Trim() }
if (-not $Version) { throw "<Version> not found in $Csproj" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$Version' is not canonical MAJOR.MINOR.PATCH"
}
Step "Deckle v$Version"
$tag       = "v$Version"
$ZipName   = "Deckle-v$Version.zip"
$ShaName   = "$ZipName.sha256"
$SetupName = "Deckle-Setup-v$Version-win-x64.exe"

# ── Resolve owner/repo from the git remote ───────────────────────────────────
$OwnerRepo = '<owner>/deckle'
$remoteUrl = & git -C $RepoRoot remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0 -and $remoteUrl) {
    if ($remoteUrl -match 'github\.com[:/]([^/]+/[^/.]+)') { $OwnerRepo = $Matches[1] }
}

# A public release is immutable input to remote machines. Prove its source
# before stopping Deckle or spending minutes in NativeAOT: clean main (including
# untracked files), synchronized origin/main, a strictly newer version, a
# non-conflicting tag, and working GitHub authentication for the exact repo.
if ($Publish) {
    $publishedTags = @(Get-PublishedReleaseTags -RepoRoot $RepoRoot)
    if (-not $publishedTags.Count) { throw 'release-history.json has no public release' }
    $releaseRecorded = $publishedTags -contains $tag

    Step 'Fetch and validate release source'
    & git -C $RepoRoot fetch origin main --tags --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin main --tags failed (code $LASTEXITCODE)" }
    $source = if ($releaseRecorded) {
        Assert-DeckleReleaseRepositorySource -RepoRoot $RepoRoot -AllowAhead
    } else {
        Assert-DeckleReleaseSource `
            -RepoRoot $RepoRoot `
            -Version $Version `
            -LatestPublishedTag $publishedTags[-1]
    }
    $OwnerRepo = $source.OwnerRepo
    Ok "clean main at $($source.HeadSha.Substring(0, 12)), synchronized with origin/main"

    & gh auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated for github.com' }
    $resolvedRepo = (& gh repo view $OwnerRepo --json nameWithOwner --jq '.nameWithOwner').Trim()
    if ($LASTEXITCODE -ne 0 -or $resolvedRepo -cne $OwnerRepo) {
        throw "GitHub repository preflight failed for $OwnerRepo"
    }
    Ok "GitHub access verified for $OwnerRepo"

    # Reconcile before building. A previous run may have stopped after upload,
    # tag publication, GitHub finalization, or local release recording. Remote
    # artifacts are downloaded and verified so a retry never trusts metadata
    # alone and never rebuilds or reuploads an existing release.
    $remoteReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish 2>$null) -join "`n"
    $remoteRelease = if ($LASTEXITCODE -eq 0) { $remoteReleaseJson | ConvertFrom-Json } else { $null }
    $recoveryPlan = Get-DeckleReleaseRecoveryPlan -Recorded $releaseRecorded -Release $remoteRelease
    if ($recoveryPlan -ceq 'Inconsistent') {
        throw "$tag is recorded locally but no matching GitHub release exists"
    }
    if ($recoveryPlan -cne 'Build') {
        Step "Reconcile existing GitHub release $tag"
        $releaseHeadSha = (& git -C $RepoRoot rev-parse "$($remoteRelease.targetCommitish)^{commit}").Trim()
        if ($LASTEXITCODE -ne 0) { throw "GitHub release target could not be resolved locally" }
        & git -C $RepoRoot merge-base --is-ancestor $releaseHeadSha $source.HeadSha
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub release target $releaseHeadSha is not an ancestor of current main $($source.HeadSha)"
        }
        Assert-DeckleReleaseAssets `
            -Release $remoteRelease `
            -Tag $tag `
            -HeadSha $releaseHeadSha `
            -ExpectedNames @($SetupName, $ZipName, $ShaName)

        $recoveryDir = Join-Path ([IO.Path]::GetTempPath()) "deckle-release-recovery-$([guid]::NewGuid())"
        try {
            $null = New-Item -ItemType Directory -Path $recoveryDir
            foreach ($assetName in @($SetupName, $ZipName, $ShaName)) {
                & gh release download $tag --repo $OwnerRepo --dir $recoveryDir --pattern $assetName
                if ($LASTEXITCODE -ne 0) { throw "GitHub asset download failed for $assetName" }
            }
            $recoveredZip = Join-Path $recoveryDir $ZipName
            $recoveredSha = Join-Path $recoveryDir $ShaName
            $recoveredSetup = Join-Path $recoveryDir $SetupName
            Assert-DeckleReleaseArchive -ZipPath $recoveredZip
            if (-not (Test-Path -LiteralPath $recoveredSetup -PathType Leaf) -or
                (Get-Item -LiteralPath $recoveredSetup).Length -le 0) {
                throw "Recovered installer is missing or empty: $SetupName"
            }
            $sidecar = (Get-Content -LiteralPath $recoveredSha -Raw).Trim()
            $actualHash = (Get-FileHash -LiteralPath $recoveredZip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($sidecar -cne "$actualHash *$ZipName") {
                throw "Recovered payload checksum does not match $ShaName"
            }
            Ok 'Existing installer, payload archive, and checksum verified'
        } finally {
            if (Test-Path -LiteralPath $recoveryDir) {
                Remove-Item -LiteralPath $recoveryDir -Recurse -Force
            }
        }

        Publish-DeckleReleaseTag -RepoRoot $RepoRoot -Tag $tag -HeadSha $releaseHeadSha
        if ($remoteRelease.isDraft) {
            & gh release edit $tag --repo $OwnerRepo --draft=false
            if ($LASTEXITCODE -ne 0) {
                throw "GitHub draft finalization failed (code $LASTEXITCODE); the verified draft remains hidden"
            }
            Ok "GitHub release $tag made public"
        }
        & (Join-Path $ScriptDir 'record-release.ps1') -Target $RepoRoot -Version $Version -Push
        if (-not $?) { throw 'record-release.ps1 failed after GitHub publication' }
        Ok "$tag recorded locally and synchronized"
        $Published = $true
        Write-DeckleActionSummary `
            -Workflow $Workflow `
            -Result Success `
            -Sentence "Deckle $tag was reconciled without rebuilding or reuploading artifacts." `
            -Details ([ordered]@{
                Worktree = $RepoRoot
                Version = $tag
                Published = 'Yes'
                'Release URL' = "https://github.com/$OwnerRepo/releases/tag/$tag"
            }) `
            -Next @('No release repair remains.')
        return
    }

    # The app payload deliberately excludes whisper.cpp. Prove the separately
    # versioned bundle is publicly downloadable and byte-for-byte identical to
    # the metadata compiled into Deckle before releasing an installer that
    # depends on it during first run.
    Step 'Verify native runtime release'
    $nativeSource = Join-Path $RepoRoot 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
    $nativeBundle = Get-DeckleNativeRuntimeBundle -SourcePath $nativeSource
    $nativeDownload = Join-Path ([IO.Path]::GetTempPath()) "deckle-native-preflight-$([guid]::NewGuid()).zip"
    try {
        Invoke-WebRequest -Uri $nativeBundle.Url -OutFile $nativeDownload
        $verifiedNative = Assert-DeckleNativeRuntimeArtifact `
            -Bundle $nativeBundle `
            -ArtifactPath $nativeDownload
        Ok "native-v$($verifiedNative.Version) available and verified ($($verifiedNative.SizeBytes) bytes)"
    } finally {
        if (Test-Path -LiteralPath $nativeDownload) {
            Remove-Item -LiteralPath $nativeDownload -Force
        }
    }
}

# ── Output layout ────────────────────────────────────────────────────────────
if (-not $OutDir) {
    # Folder name = the ZIP stem (Deckle-v<X.Y.Z>) so the artefact layout is
    # coherent end to end: artifacts\Deckle-v0.4.0\Deckle-v0.4.0.zip, matching
    # the release tag v<X.Y.Z> and the asset download URL.
    $OutDir = Join-Path $RepoRoot "artifacts\Deckle-v$Version"
}
if (Test-Path $OutDir) {
    Warn "OutDir exists, cleaning: $OutDir"
    Remove-Item $OutDir -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $OutDir

$PublishDir = Join-Path $OutDir 'publish'
$ZipPath    = Join-Path $OutDir $ZipName
$ShaPath    = Join-Path $OutDir $ShaName

# Headline asset: the installer stub, named the OSS-classic way (version + arch)
# so it reads as "the thing to download" among the release files.
$SetupPath       = Join-Path $OutDir $SetupName
$InstallerCsproj = Join-Path $RepoRoot 'src\Deckle.Installer\Deckle.Installer.csproj'
$InstallerPubDir = Join-Path $OutDir 'installer'

# Generate or validate notes before stopping the running app. A changelog
# failure is a source failure, not a reason to interrupt Deckle.
if ($Publish) {
    if (-not $Notes) {
        $Notes = Join-Path $OutDir 'release-notes.md'
        & (Join-Path $ScriptDir 'changelog.ps1') -Target $RepoRoot -NotesFor $Version -OutFile $Notes
        if ($LASTEXITCODE -ne 0) { throw "changelog.ps1 notes generation failed (code $LASTEXITCODE)" }
        Ok "Release notes generated from history: $Notes"
    } elseif (-not (Test-Path -LiteralPath $Notes -PathType Leaf)) {
        throw "Release notes file not found: $Notes"
    }
}

# Publishing rebuilds the app payload. A running Release instance keeps the
# current app DLLs locked and makes MSBuild retry for a minute before failing.
Step 'Stop running Deckle instance'
Stop-DeckleProcess -WriteOk ${function:Ok} -WriteWarn ${function:Warn}

# ── Publish: self-contained, unpackaged, folder (no PublishSingleFile) ───────
# win-x64 via RuntimeIdentifierOverride, NOT `-r win-x64`. A plain RID
# propagates to the ProjectReferences, landing each generated .pri under a
# per-RID bin subfolder (bin\...\win-x64\). The head project's PRI merge then
# looks for some of them at the non-RID path and fails with PRI252 — this is
# WindowsAppSDK issue #3337. RuntimeIdentifierOverride is Microsoft's
# documented workaround: it sets the RID without the path-splitting
# propagation. SelfContained is forced (a RID no longer implies it since
# .NET 6); the app is x64-only (<Platforms>x64</Platforms>). Restore implicit.
Step 'dotnet publish (Release, win-x64, self-contained folder)'
& dotnet publish $Csproj `
    '-c:Release' `
    '-p:RuntimeIdentifierOverride=win-x64' `
    '-p:SelfContained=true' `
    '-p:Platform=x64' `
    '-o' $PublishDir `
    '-v:m' '-nologo' `
    '/nr:false' '/p:UseSharedCompilation=false'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (code $LASTEXITCODE)" }

# ── Sanity: the two files a misconfigured publish silently drops ─────────────
$exe = Join-Path $PublishDir 'Deckle.exe'
$pri = Join-Path $PublishDir 'Deckle.pri'
if (-not (Test-Path $exe)) { throw "Deckle.exe missing from publish output — $exe" }
# Deckle.pri carries every compiled XAML resource (.xbf) since WinAppSDK 1.8.
# Without it the app launches windowless. EnableMsixTooling=true in the csproj
# is what makes Publish emit it; treat its absence as a hard failure.
if (-not (Test-Path $pri)) { throw "Deckle.pri missing — windowless app. Check EnableMsixTooling in the csproj." }
$fileCount = (Get-ChildItem $PublishDir -Recurse -File).Count
Ok "publish folder OK — $fileCount files (Deckle.exe + Deckle.pri present)"

# ── Zip the folder + zip-level SHA256 ────────────────────────────────────────
# CreateFromDirectory (streaming) rather than Compress-Archive: the publish
# folder is ~300 MB across 100s of files, where Compress-Archive is slow and
# memory-hungry. includeBaseDirectory:$false puts Deckle.exe at the zip root so
# the installer extracts straight into the target folder.
Step "Compress to $ZipName"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $PublishDir, $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

# Re-open the exact archive the installer will consume. This catches truncated
# zips, missing root payload files, duplicate paths and archive traversal before
# a checksum blesses the broken artifact.
Assert-DeckleReleaseArchive -PublishDir $PublishDir -ZipPath $ZipPath
Ok 'release archive reopened and its payload contract verified'

$ZipSha256 = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$ZipBytes  = (Get-Item $ZipPath).Length
$ZipSize   = [math]::Round($ZipBytes / 1MB, 2)
Ok "$ZipName ($ZipSize MB) sha256=$ZipSha256"

# sha256sum -c compatible sidecar, so the installer (or a human) can verify the
# download independently of the release body.
Set-Content -Path $ShaPath -Value ('{0} *{1}' -f $ZipSha256, $ZipName) -Encoding ascii
Ok "$ShaName written"

# ── Build the installer stub (NativeAOT, win-x64) — the headline asset ───────
# The file the end user downloads and runs. A standalone native exe (PublishAot
# in src/Deckle.Installer): it resolves the latest GitHub Release, downloads the
# app ZIP above, verifies it against the .sha256 sidecar, extracts it per-user,
# and registers Deckle. NativeAOT links here at publish — it needs the VC++
# linker on the maintainer's machine; a plain `dotnet build` only validates the
# IL. `-r win-x64` is correct here: the installer has no PRI merge, so none of
# the WinAppSDK RID quirk the app build dodges with RuntimeIdentifierOverride.
# x64-only matches the app: the payload it fetches is x64-only too.
Step 'dotnet publish installer (Release, win-x64, NativeAOT)'
if (-not (Test-Path $InstallerCsproj)) { throw "Installer csproj not found at $InstallerCsproj" }
& dotnet publish $InstallerCsproj `
    '-c:Release' `
    '-r' 'win-x64' `
    '-o' $InstallerPubDir `
    '-v:m' '-nologo' `
    '/nr:false' '/p:UseSharedCompilation=false'
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (installer) failed (code $LASTEXITCODE)" }

# AssemblyName is Deckle-Installer → the linker emits Deckle-Installer.exe. Copy
# it up to the version- and arch-tagged release name. The exe reads its own path
# at runtime (it self-copies as the uninstaller), so the download name is free
# to be descriptive.
$InstallerBuilt = Join-Path $InstallerPubDir 'Deckle-Installer.exe'
if (-not (Test-Path $InstallerBuilt)) { throw "Installer exe missing from publish output — $InstallerBuilt" }
Copy-Item $InstallerBuilt $SetupPath -Force
$SetupBytes = (Get-Item $SetupPath).Length
$SetupSize  = [math]::Round($SetupBytes / 1MB, 2)
$SetupSha256 = (Get-FileHash $SetupPath -Algorithm SHA256).Hash.ToLower()
Ok "$SetupName ($SetupSize MB) sha256=$SetupSha256"

# ── Summary — release convention the installer / future updater consumes ─────
Step 'Done'
Write-Host @"

  Installer : $SetupPath ($SetupSize MB)   <- the user-facing download
              sha256=$SetupSha256
  Payload   : $ZipPath ($ZipSize MB)
  SHA256    : $ZipSha256

  Release convention (asset URLs the installer resolves):
    https://github.com/$OwnerRepo/releases/download/v$Version/$ZipName
    https://github.com/$OwnerRepo/releases/download/v$Version/$ShaName

  To publish (maintainer act):
    pwsh scripts/lib/publish-app.ps1 -Target "$RepoRoot" -Publish

"@ -ForegroundColor Green

# ── Optional: gh release create (maintainer act, never run by tooling) ───────
if ($Publish) {
    Step "Publish GitHub Release v$Version"
    $tag    = "v$Version"
    $title  = "Deckle $tag"
    # Asset order = upload order = display order on the release page: the
    # installer exe first (the headline download), then the payload + its sha.
    $headSha = (& git -C $RepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "git rev-parse HEAD failed (code $LASTEXITCODE)" }
    $ghArgs = @(
        'release', 'create', $tag, $SetupPath, $ZipPath, $ShaPath,
        '--repo', $OwnerRepo,
        '--title', $title,
        '--target', $headSha,
        '--draft')
    # Every 0.x cut is a pre-release (versioning convention): the phase is
    # pre-stable, so the release must not claim the repo's "Latest" badge.
    if ($Version -like '0.*') { $ghArgs += '--prerelease' }
    $ghArgs += @('--notes-file', $Notes)
    $remoteReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish 2>$null) -join "`n"
    $releaseExists = $LASTEXITCODE -eq 0
    if (-not $releaseExists) {
        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed (code $LASTEXITCODE)" }
        Ok "Draft release $tag uploaded"

        $remoteReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "GitHub draft $tag could not be read after upload" }
    } else {
        Ok "Existing draft release $tag found; validating it for resume"
    }

    # Read the draft back from GitHub before it can become discoverable by the
    # installer. Its source, names and byte sizes must match the local release.
    $remoteRelease = $remoteReleaseJson | ConvertFrom-Json
    $expectedAssets = @{
        $SetupName = $SetupBytes
        $ZipName   = $ZipBytes
        $ShaName   = (Get-Item $ShaPath).Length
    }
    Assert-DeckleReleaseDraft `
        -Release $remoteRelease `
        -Tag $tag `
        -HeadSha $headSha `
        -ExpectedAssets $expectedAssets
    Ok 'GitHub draft assets verified by name and byte size'

    Publish-DeckleReleaseTag -RepoRoot $RepoRoot -Tag $tag -HeadSha $headSha
    Ok "GitHub tag $tag published at release HEAD"

    Step 'Make the verified GitHub release public'
    $finalizeArgs = @('release', 'edit', $tag, '--repo', $OwnerRepo, '--draft=false')
    if ($Version -like '0.*') { $finalizeArgs += '--prerelease' }
    & gh @finalizeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub draft finalization failed (code $LASTEXITCODE); the verified draft remains hidden"
    }
    $publicReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Published GitHub release $tag could not be read back" }
    $publicRelease = $publicReleaseJson | ConvertFrom-Json
    if ($publicRelease.isDraft) { throw "GitHub release $tag is still a draft after finalization" }
    Assert-DeckleReleaseAssets `
        -Release $publicRelease `
        -Tag $tag `
        -HeadSha $headSha `
        -ExpectedNames @($SetupName, $ZipName, $ShaName) `
        -ExpectedSizes $expectedAssets

    Step 'Freeze the public release into CHANGELOG.md'
    & (Join-Path $ScriptDir 'record-release.ps1') -Target $RepoRoot -Version $Version -Push
    if (-not $?) { throw 'record-release.ps1 failed after GitHub publication' }
    $Published = $true
    Ok "Released as $tag"
}

$releaseTag = if ($Version) { "v$Version" } else { $null }
$releaseUrl = if ($Published) { "https://github.com/$OwnerRepo/releases/tag/$releaseTag" } else { $null }
$sentence = if ($Published) {
    "Deckle $releaseTag was built and published as a GitHub Release."
} else {
    "Deckle $releaseTag release artifacts were built locally for inspection."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Worktree      = $RepoRoot
        Version       = $releaseTag
        OutDir        = $OutDir
        Installer     = $SetupPath
        Payload       = $ZipPath
        SHA256        = $ZipSha256
        'Installer SHA256' = $SetupSha256
        Published     = $(if ($Published) { 'Yes' } else { 'No' })
        'Release URL' = $releaseUrl
    }) `
    -Next $(if (-not $Published) { @("Run with -Publish, or use the menu: Release > Publish app release.") } else { @("Verify the GitHub release page and downloaded installer asset.") })
