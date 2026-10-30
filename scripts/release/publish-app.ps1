<#
.SYNOPSIS
    Prepare or publish the app release
.DESCRIPTION
    Builds the two artifacts a Deckle GitHub Release carries: the installer
    stub and the self-contained app ZIP with its sha256 sidecar. Without
    -Publish both are built locally for inspection; with -Publish they are
    also uploaded as a GitHub Release under the version tag via gh.
.NOTES
    section: Release
    order: 110
    action: Prepare app artifacts |
    action: Publish app release | -Publish | danger | Publishes a public GitHub Release of the app.
#>

# The app payload is the publish folder, not a single file: the rationale sits
# in the _IsPublishing PropertyGroup of Deckle.App.csproj. The whisper.cpp
# native runtime and the Whisper models are not shipped: the setup wizard
# provisions them per user, and the native bundle has its own release cycle
# (publish-native-runtime.ps1, native-vX.Y.Z tags).

[CmdletBinding()]
param(
    # Build a specific repo or worktree instead of the one containing this
    # script. Accepts any path — main repo or any git worktree root.
    [string]$Target,

    # Output directory for the publish folder + produced ZIP. Defaults to a
    # versioned subfolder under <RepoRoot>\artifacts\ (git-ignored). Never the
    # system temp dir — build artefacts stay on the repo's volume, off C:.
    [string]$OutDir,

    # Also publish the installer exe + the app ZIP (+ its .sha256 sidecar) as a
    # GitHub Release tagged v$Version via gh. Requires gh authenticated against
    # the repo's remote. The version is the vX.Y.Z tag HEAD carries; gh attaches
    # to the commit only after every artifact has built successfully.
    [switch]$Publish,

    # Optional release-notes file passed to `gh release create --notes-file`.
    # Without it, the notes are generated from the commit history by
    # scripts/git/changelog.ps1 (plain git log, no GitHub API).
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot                                  # scripts/release/
$ScriptsDir = Split-Path -Parent $ScriptDir                 # scripts/
$GitDir = Join-Path $ScriptsDir 'git'                       # the versions pack
$LibDir = Join-Path $ScriptsDir 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'deckle-process.ps1')
. (Join-Path $LibDir 'native-console.ps1')
. (Join-Path $LibDir 'version.ps1')
Import-Module (Join-Path $LibDir 'release-validation.psm1') -Force
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force

$WorkflowOutput = New-WorkflowOutput -Category 'publish'

$Workflow = if ($Publish) { 'Publish app release' } else { 'Prepare app release artifacts' }
$RepoRoot = $null
$Version = $null
$SetupPath = $null
$ZipPath = $null
$ZipSha256 = $null
$Published = $false

trap {
    Write-ActionSummary `
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

if ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent $ScriptsDir
}

# Once GitHub holds the public release, the repository records it: the
# annotated tag release/vX.Y.Z goes on the released commit and is pushed, then
# CHANGELOG.md is regenerated from the tags and committed, so the two records
# cannot disagree. Called after the release is public, so a failure here is a
# repair to make, never a reason to retract the release.
function Register-PublishedRelease {
    param([Parameter(Mandatory)][string]$ReleaseSha)

    Publish-DeckleReleaseTag -RepoRoot $RepoRoot -Tag "release/$tag" -HeadSha $ReleaseSha -Message "Release $Version"
    Write-WorkflowMessage -Output $WorkflowOutput -Message "release/$tag marked at $($ReleaseSha.Substring(0, 12)) and pushed"

    & (Join-Path $GitDir 'changelog.ps1') -Target $RepoRoot -Apply
    if ($LASTEXITCODE -ne 0) { throw "changelog.ps1 failed after GitHub publication (code $LASTEXITCODE)" }
    $changed = @(& git -C $RepoRoot status --porcelain -- CHANGELOG.md)
    if ($changed.Count -eq 0) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message 'CHANGELOG.md already carried the release' -Role Muted
        return
    }
    & git -C $RepoRoot add -- CHANGELOG.md
    if ($LASTEXITCODE -ne 0) { throw 'git add CHANGELOG.md failed after GitHub publication' }
    & git -C $RepoRoot commit -q -m "docs(changelog): record $tag"
    if ($LASTEXITCODE -ne 0) { throw 'git commit of CHANGELOG.md failed after GitHub publication' }
    & git -C $RepoRoot push origin main
    if ($LASTEXITCODE -ne 0) { throw 'git push of the changelog commit failed after GitHub publication' }
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'CHANGELOG.md regenerated, committed and pushed'
}

Write-OutputText -Text "Repo: $RepoRoot" -Role Muted

$ProjectDir = Join-Path $RepoRoot 'src\Deckle.App'
$Csproj     = Join-Path $ProjectDir 'Deckle.App.csproj'
if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle repo?" }

# The version is the vX.Y.Z tag on HEAD, recorded by scripts/git/record-version.ps1
# at the merge into main: a release is cut from the commit that carries it, and
# the build reads the same tag. The reader throws its own sentence when HEAD
# carries none.
$Version = Get-RepositoryVersion -Path $RepoRoot
Write-WorkflowStep -Output $WorkflowOutput -Message "Deckle v$Version"
$tag       = "v$Version"
$ZipName   = "Deckle-v$Version.zip"
$ShaName   = "$ZipName.sha256"
$SetupName = "Deckle-Setup-v$Version-win-x64.exe"

$OwnerRepo = '<owner>/deckle'
$remoteUrl = & git -C $RepoRoot remote get-url origin 2>$null
if ($LASTEXITCODE -eq 0 -and $remoteUrl) {
    if ($remoteUrl -match 'github\.com[:/]([^/]+/[^/.]+)') { $OwnerRepo = $Matches[1] }
}

# A public release is immutable input to remote machines: prove its source
# before stopping Deckle or spending minutes in NativeAOT.
if ($Publish) {
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Fetch and validate release source'
    & git -C $RepoRoot fetch origin main --tags --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin main --tags failed (code $LASTEXITCODE)" }

    # A public release is marked by an annotated tag release/vX.Y.Z on its
    # commit; the marks are the only record, read after the fetch so a mark
    # pushed from another machine counts.
    $publishedTags = @(Get-DeckleReleaseTags -RepoRoot $RepoRoot)
    if (-not $publishedTags.Count) { throw 'No release/vX.Y.Z tag marks a public release' }
    $releaseRecorded = $publishedTags -contains $tag

    $source = if ($releaseRecorded) {
        Assert-DeckleReleaseRepositorySource -RepoRoot $RepoRoot -AllowAhead
    } else {
        Assert-DeckleReleaseSource `
            -RepoRoot $RepoRoot `
            -Version $Version `
            -LatestPublishedTag $publishedTags[-1]
    }
    $OwnerRepo = $source.OwnerRepo
    Write-WorkflowMessage -Output $WorkflowOutput -Message "clean main at $($source.HeadSha.Substring(0, 12)), synchronized with origin/main"

    & gh auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated for github.com' }
    $resolvedRepo = (& gh repo view $OwnerRepo --json nameWithOwner --jq '.nameWithOwner').Trim()
    if ($LASTEXITCODE -ne 0 -or $resolvedRepo -cne $OwnerRepo) {
        throw "GitHub repository preflight failed for $OwnerRepo"
    }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "GitHub access verified for $OwnerRepo"

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
        Write-WorkflowStep -Output $WorkflowOutput -Message "Reconcile existing GitHub release $tag"
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
            Write-WorkflowMessage -Output $WorkflowOutput -Message 'Existing installer, payload archive, and checksum verified'
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
            Write-WorkflowMessage -Output $WorkflowOutput -Message "GitHub release $tag made public"
        }
        Register-PublishedRelease -ReleaseSha $releaseHeadSha
        Write-WorkflowMessage -Output $WorkflowOutput -Message "$tag marked as released and synchronized"
        $Published = $true
        Write-ActionSummary `
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

    # The setup wizard downloads the separately versioned whisper.cpp bundle:
    # prove it is publicly downloadable and byte-for-byte identical to the
    # metadata compiled into Deckle before releasing an installer that needs it.
    Write-WorkflowStep -Output $WorkflowOutput -Message 'Verify native runtime release'
    $nativeSource = Join-Path $RepoRoot 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
    $nativeBundle = Get-DeckleNativeRuntimeBundle -SourcePath $nativeSource
    $nativeDownload = Join-Path ([IO.Path]::GetTempPath()) "deckle-native-preflight-$([guid]::NewGuid()).zip"
    try {
        Invoke-WebRequest -Uri $nativeBundle.Url -OutFile $nativeDownload
        $verifiedNative = Assert-DeckleNativeRuntimeArtifact `
            -Bundle $nativeBundle `
            -ArtifactPath $nativeDownload
        Write-WorkflowMessage -Output $WorkflowOutput -Message "native-v$($verifiedNative.Version) available and verified ($($verifiedNative.SizeBytes) bytes)"
    } finally {
        if (Test-Path -LiteralPath $nativeDownload) {
            Remove-Item -LiteralPath $nativeDownload -Force
        }
    }
}

if (-not $OutDir) {
    $OutDir = Join-Path $RepoRoot "artifacts\Deckle-v$Version"
}
if (Test-Path $OutDir) {
    Write-WorkflowMessage -Output $WorkflowOutput -Message "OutDir exists, cleaning: $OutDir" -Role Warning
    Remove-Item $OutDir -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $OutDir

$PublishDir = Join-Path $OutDir 'publish'
$ZipPath    = Join-Path $OutDir $ZipName
$ShaPath    = Join-Path $OutDir $ShaName

$SetupPath       = Join-Path $OutDir $SetupName
$InstallerCsproj = Join-Path $RepoRoot 'src\Deckle.Installer\Deckle.Installer.csproj'
$InstallerPubDir = Join-Path $OutDir 'installer'

# Generate or validate notes before stopping the running app. A changelog
# failure is a source failure, not a reason to interrupt Deckle.
if ($Publish) {
    if (-not $Notes) {
        $Notes = Join-Path $OutDir 'release-notes.md'
        & (Join-Path $GitDir 'changelog.ps1') -Target $RepoRoot -NotesFor $Version -OutFile $Notes -Apply
        if ($LASTEXITCODE -ne 0) { throw "changelog.ps1 notes generation failed (code $LASTEXITCODE)" }
        Write-WorkflowMessage -Output $WorkflowOutput -Message "Release notes generated from history: $Notes"
    } elseif (-not (Test-Path -LiteralPath $Notes -PathType Leaf)) {
        throw "Release notes file not found: $Notes"
    }
}

# A running Release instance keeps the app DLLs locked, and the publish below
# would retry the copy until it fails.
Write-WorkflowStep -Output $WorkflowOutput -Message 'Stop running Deckle instance'
Stop-DeckleProcess -WriteEvent {
    param([string]$Role, [string]$Message)
    Write-WorkflowMessage -Output $WorkflowOutput -Message $Message -Role $Role
}

# win-x64 via RuntimeIdentifierOverride, NOT `-r win-x64`. A plain RID
# propagates to the ProjectReferences, landing each generated .pri under a
# per-RID bin subfolder (bin\...\win-x64\). The head project's PRI merge then
# looks for some of them at the non-RID path and fails with PRI252 — this is
# WindowsAppSDK issue #3337. RuntimeIdentifierOverride is Microsoft's
# documented workaround: it sets the RID without the path-splitting
# propagation. SelfContained is forced (a RID no longer implies it since
# .NET 6); the app is x64-only (<Platforms>x64</Platforms>).
Write-WorkflowStep -Output $WorkflowOutput -Message 'dotnet publish (Release, win-x64, self-contained folder)'
$publishExitCode = Invoke-CapturedProcess -FilePath 'dotnet' -ArgumentList @(
    'publish', $Csproj,
    '-c:Release',
    '-p:RuntimeIdentifierOverride=win-x64',
    '-p:SelfContained=true',
    '-p:Platform=x64',
    '-o', $PublishDir,
    '-v:m', '-nologo',
    '/m:4'
)
if ($publishExitCode -ne 0) { throw "dotnet publish failed (code $publishExitCode)" }

# A misconfigured publish drops these two files silently.
$exe = Join-Path $PublishDir 'Deckle.exe'
$pri = Join-Path $PublishDir 'Deckle.pri'
if (-not (Test-Path $exe)) { throw "Deckle.exe missing from publish output — $exe" }
# Deckle.pri carries every compiled XAML resource (.xbf) since WinAppSDK 1.8.
# Without it the app launches windowless. EnableMsixTooling=true in the csproj
# is what makes Publish emit it; treat its absence as a hard failure.
if (-not (Test-Path $pri)) { throw "Deckle.pri missing — windowless app. Check EnableMsixTooling in the csproj." }
$fileCount = (Get-ChildItem $PublishDir -Recurse -File).Count
Write-WorkflowMessage -Output $WorkflowOutput -Message "publish folder OK — $fileCount files (Deckle.exe + Deckle.pri present)"

# CreateFromDirectory (streaming) rather than Compress-Archive, which is slow
# and memory-hungry on a publish folder of hundreds of files.
# includeBaseDirectory:$false puts Deckle.exe at the zip root so the installer
# extracts straight into the target folder.
Write-WorkflowStep -Output $WorkflowOutput -Message "Compress to $ZipName"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $PublishDir, $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)

# Re-open the exact archive the installer will consume. This catches truncated
# zips, missing root payload files, duplicate paths and archive traversal before
# a checksum blesses the broken artifact.
Assert-DeckleReleaseArchive -PublishDir $PublishDir -ZipPath $ZipPath
Write-WorkflowMessage -Output $WorkflowOutput -Message 'release archive reopened and its payload contract verified'

$ZipSha256 = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$ZipBytes  = (Get-Item $ZipPath).Length
$ZipSize   = [math]::Round($ZipBytes / 1MB, 2)
Write-WorkflowMessage -Output $WorkflowOutput -Message "$ZipName ($ZipSize MB) sha256=$ZipSha256"

# sha256sum -c compatible sidecar, so the installer (or a human) can verify the
# download independently of the release body.
Set-Content -Path $ShaPath -Value ('{0} *{1}' -f $ZipSha256, $ZipName) -Encoding ascii
Write-WorkflowMessage -Output $WorkflowOutput -Message "$ShaName written"

# NativeAOT links the installer stub here at publish: it needs the VC++ linker
# on the maintainer's machine; a plain `dotnet build` only validates the IL.
# `-r win-x64` is correct here: the installer has no PRI merge, so none of the
# WinAppSDK RID quirk the app build dodges with RuntimeIdentifierOverride.
Write-WorkflowStep -Output $WorkflowOutput -Message 'dotnet publish installer (Release, win-x64, NativeAOT)'
if (-not (Test-Path $InstallerCsproj)) { throw "Installer csproj not found at $InstallerCsproj" }
$installerExitCode = Invoke-CapturedProcess -FilePath 'dotnet' -ArgumentList @(
    'publish', $InstallerCsproj,
    '-c:Release',
    '-r', 'win-x64',
    '-o', $InstallerPubDir,
    '-v:m', '-nologo',
    '/m:4'
)
if ($installerExitCode -ne 0) { throw "dotnet publish (installer) failed (code $installerExitCode)" }

# The exe reads its own path at runtime (it copies itself as the uninstaller),
# so the download name is free to carry the version and the arch.
$InstallerBuilt = Join-Path $InstallerPubDir 'Deckle-Installer.exe'
if (-not (Test-Path $InstallerBuilt)) { throw "Installer exe missing from publish output — $InstallerBuilt" }
Copy-Item $InstallerBuilt $SetupPath -Force
$SetupBytes = (Get-Item $SetupPath).Length
$SetupSize  = [math]::Round($SetupBytes / 1MB, 2)
$SetupSha256 = (Get-FileHash $SetupPath -Algorithm SHA256).Hash.ToLower()
Write-WorkflowMessage -Output $WorkflowOutput -Message "$SetupName ($SetupSize MB) sha256=$SetupSha256"

Write-WorkflowStep -Output $WorkflowOutput -Message 'Done'
Write-Host @"

  Installer : $SetupPath ($SetupSize MB)   <- the user-facing download
              sha256=$SetupSha256
  Payload   : $ZipPath ($ZipSize MB)
  SHA256    : $ZipSha256

  Release convention (asset URLs the installer resolves):
    https://github.com/$OwnerRepo/releases/download/v$Version/$ZipName
    https://github.com/$OwnerRepo/releases/download/v$Version/$ShaName

  To publish (maintainer act):
    pwsh scripts/release/publish-app.ps1 -Target "$RepoRoot" -Publish

"@ -ForegroundColor Green

# Publishing is the maintainer's act, never triggered by an agent.
if ($Publish) {
    Write-WorkflowStep -Output $WorkflowOutput -Message "Publish GitHub Release v$Version"
    $tag    = "v$Version"
    $title  = "Deckle $tag"
    $headSha = (& git -C $RepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "git rev-parse HEAD failed (code $LASTEXITCODE)" }
    # Asset order = upload order = display order on the release page: the
    # installer exe first (the headline download), then the payload + its sha.
    $ghArgs = @(
        'release', 'create', $tag, $SetupPath, $ZipPath, $ShaPath,
        '--repo', $OwnerRepo,
        '--title', $title,
        '--target', $headSha,
        '--draft',
        '--notes-file', $Notes)
    $remoteReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish 2>$null) -join "`n"
    $releaseExists = $LASTEXITCODE -eq 0
    if (-not $releaseExists) {
        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed (code $LASTEXITCODE)" }
        Write-WorkflowMessage -Output $WorkflowOutput -Message "Draft release $tag uploaded"

        $remoteReleaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "GitHub draft $tag could not be read after upload" }
    } else {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "Existing draft release $tag found; validating it for resume"
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
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'GitHub draft assets verified by name and byte size'

    Publish-DeckleReleaseTag -RepoRoot $RepoRoot -Tag $tag -HeadSha $headSha
    Write-WorkflowMessage -Output $WorkflowOutput -Message "GitHub tag $tag published at release HEAD"

    Write-WorkflowStep -Output $WorkflowOutput -Message 'Make the verified GitHub release public'
    # A 0.x cut is a release, not a GitHub pre-release: SemVer's 0.x already says
    # the surface may still move, and the pre-release flag (versioning convention)
    # is kept for a suffixed build sent out for testing. "Latest" is claimed
    # explicitly because the repo also carries native-v* tags, whose ordering
    # against vX.Y.Z under GitHub's automatic rule is not something to rely on.
    & gh release edit $tag --repo $OwnerRepo --draft=false --latest
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

    Write-WorkflowStep -Output $WorkflowOutput -Message 'Mark the release and freeze it into CHANGELOG.md'
    Register-PublishedRelease -ReleaseSha $headSha
    $Published = $true
    Write-WorkflowMessage -Output $WorkflowOutput -Message "Released as $tag"
}

$releaseTag = if ($Version) { "v$Version" } else { $null }
$releaseUrl = if ($Published) { "https://github.com/$OwnerRepo/releases/tag/$releaseTag" } else { $null }
$sentence = if ($Published) {
    "Deckle $releaseTag was built and published as a GitHub Release."
} else {
    "Deckle $releaseTag release artifacts were built locally for inspection."
}

Write-ActionSummary `
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
