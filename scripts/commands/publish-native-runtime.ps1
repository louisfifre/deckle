# publish-native-runtime.ps1
#
# Packages a versioned zip of the native DLLs Deckle's first-run wizard
# downloads at install time. The wizard fetches this zip from a GitHub
# Release of the Deckle repo (URL hardcoded in NativeRuntime.CurrentBundle)
# and extracts it into <UserDataRoot>\native\.
#
# Bundle layout (flat, matches NativeRuntime.CopyFromFolder semantics):
#   libwhisper.dll, ggml.dll, ggml-base.dll, ggml-cpu.dll, ggml-vulkan.dll
#   libgcc_s_seh-1.dll, libstdc++-6.dll, libwinpthread-1.dll
#   PROVENANCE.txt   — human-readable build metadata
#   SHA256SUMS       — `sha256sum -c` compatible
#
# Sources, in priority order:
#   - current bundle: the artifact pinned by NativeRuntime.CurrentBundle
#   - new bundle    : whisper DLLs from <WhisperRepo>\build\bin\ and MinGW DLLs
#                     beside the compiler recorded in CMakeCache.txt
#
# Keep this script aligned with the native runtime bundle metadata in code.

[CmdletBinding()]
param(
    # Optional override for recovery. Normally inferred from whisper.cpp:
    # X.Y tracks its upstream minor, Z is the next local rebuild counter.
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    # Path to a local whisper.cpp clone with build/bin/ already populated by
    # `cmake --build`. Used only when packaging a new runtime bundle.
    [string]$WhisperRepo,

    # Publish or inspect an already-packaged bundle instead of rebuilding it.
    # When neither source is supplied, the bundle pinned by CurrentBundle is
    # discovered under <RepoRoot>\artifacts\deckle-native-X.Y.Z\ automatically.
    [string]$ArtifactPath,

    # Deckle repository that owns the native-vX.Y.Z GitHub Release. This is
    # source metadata only: publishing the native runtime never builds Deckle.
    [string]$Target,

    # Output directory for the produced zip. Defaults to a fresh subfolder
    # under the system temp dir.
    [string]$OutDir,

    # Also publish the zip as a GitHub Release `native-v$Version` via gh.
    # Requires gh CLI authenticated against the repo's remote.
    [switch]$Publish,

    # Optional release notes file passed to `gh release create --notes-file`.
    # Without it, deterministic notes are written locally.
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force
Import-Module (Join-Path $LibDir 'release-validation.psm1') -Force

# ── Catalog ──────────────────────────────────────────────────────────────────
$RepoRoot = if ($Target) {
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { throw "Target not found: $Target" }
    (Get-Item -LiteralPath $Target).FullName
} else {
    Split-Path -Parent (Split-Path -Parent $ScriptDir)
}
$NativeRuntimeSource = Join-Path $RepoRoot 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
$NativeRuntimeCatalog = Get-DeckleNativeRuntimeCatalog -SourcePath $NativeRuntimeSource
$WhisperDlls = @($NativeRuntimeCatalog.WhisperDlls)
$MingwDlls = @($NativeRuntimeCatalog.MingwDlls)

$WorkflowOutput = New-DeckleWorkflowOutput -Category 'publish'

$Workflow = if ($Publish) { 'Publish native runtime release' } else { 'Prepare native runtime release' }
$ZipPath = $null
$ZipSha256 = $null
$ZipBytes = $null
$ZipSize = $null
$Published = $false
$OwnerRepo = $null
$HeadSha = $null
$SourceKind = $null

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "$Workflow failed before completion." `
        -Details ([ordered]@{
            Version     = "native-v$Version"
            DeckleRepo  = $RepoRoot
            WhisperRepo = $WhisperRepo
            Artifact    = $ArtifactPath
            OutDir      = $OutDir
            Zip         = $ZipPath
            Published   = $Published
            Error       = $_.Exception.Message
        })
    throw
}

# Publication validates the Deckle repository and GitHub destination before it
# touches the package directory. It does not invoke dotnet, CMake, Ninja, or any
# build command; build/bin is an explicit input produced beforehand.
if ($Publish) {
    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Validate native release destination'
    & git -C $RepoRoot fetch origin main --tags --prune
    if ($LASTEXITCODE -ne 0) { throw "git fetch origin main --tags failed (code $LASTEXITCODE)" }
    $source = Assert-DeckleReleaseRepositorySource -RepoRoot $RepoRoot
    $OwnerRepo = $source.OwnerRepo
    $HeadSha = $source.HeadSha
    & gh auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI is not authenticated for github.com' }
    $resolvedRepo = (& gh repo view $OwnerRepo --json nameWithOwner --jq '.nameWithOwner').Trim()
    if ($LASTEXITCODE -ne 0 -or $resolvedRepo -cne $OwnerRepo) {
        throw "GitHub repository preflight failed for $OwnerRepo"
    }
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "GitHub access verified for $OwnerRepo at $($HeadSha.Substring(0, 12))"
} else {
    $remoteUrl = & git -C $RepoRoot remote get-url origin 2>$null
    if ($LASTEXITCODE -eq 0 -and $remoteUrl -match 'github\.com[:/](?<repo>[^/]+/[^/.]+)(?:\.git)?$') {
        $OwnerRepo = $Matches.repo
    } else {
        $OwnerRepo = '<owner>/deckle'
    }
}

if ($ArtifactPath -and $WhisperRepo) {
    throw 'ArtifactPath and WhisperRepo are mutually exclusive; choose an existing bundle or a whisper.cpp build'
}

$PinnedBundle = Get-DeckleNativeRuntimeBundle -SourcePath $NativeRuntimeSource
if (-not $ArtifactPath -and -not $WhisperRepo) {
    $candidateName = "deckle-native-$($PinnedBundle.Version).zip"
    $candidatePath = Join-Path $RepoRoot "artifacts\deckle-native-$($PinnedBundle.Version)\$candidateName"
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $ArtifactPath = $candidatePath
    } else {
        throw "No current native bundle found at $candidatePath; pass -ArtifactPath or -WhisperRepo"
    }
}

if ($ArtifactPath) {
    if ($Version -and $Version -cne [string]$PinnedBundle.Version) {
        throw "Artifact version $Version does not match CurrentBundle version $($PinnedBundle.Version)"
    }
    $Version = [string]$PinnedBundle.Version
    $ZipName = "deckle-native-$Version.zip"
    $resolvedArtifact = Resolve-Path -LiteralPath $ArtifactPath -ErrorAction Stop
    $ZipPath = $resolvedArtifact.Path
    $ArtifactPath = $ZipPath
    if ((Split-Path -Leaf $ZipPath) -cne $ZipName) {
        throw "Native runtime artifact must be named $ZipName"
    }

    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Validate existing $ZipName"
    $verifiedArtifact = Assert-DeckleNativeRuntimeArtifact `
        -Bundle $PinnedBundle `
        -ArtifactPath $ZipPath
    Assert-DeckleNativeRuntimeArchive `
        -ArchivePath $ZipPath `
        -DllNames $NativeRuntimeCatalog.Names
    $ZipSha256 = $verifiedArtifact.Sha256
    $ZipBytes = $verifiedArtifact.SizeBytes
    $ZipSize = [math]::Round($ZipBytes / 1MB, 2)
    if (-not $OutDir) { $OutDir = Split-Path -Parent $ZipPath }
    $SourceKind = 'Existing artifact'
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "$ZipName ($ZipSize MB) matches CurrentBundle and the native catalog"
} else {
    if (-not $Version) {
        $publishedTags = @(& git -C $RepoRoot tag --list 'native-v*')
        if ($LASTEXITCODE -ne 0) { throw "git tag --list native-v* failed (code $LASTEXITCODE)" }
        $versionPlan = Get-DeckleNativeRuntimeVersionPlan `
            -SourcePath $NativeRuntimeSource `
            -WhisperRepo $WhisperRepo `
            -PublishedTags $publishedTags
        $Version = $versionPlan.Version
        Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Resolved native-v$Version"
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "whisper.cpp $($versionPlan.WhisperVersion); previous bundle native-v$($versionPlan.PreviousVersion)"
    }

    # ── Resolve sources ──────────────────────────────────────────────────────

    $WhisperBin = Join-Path $WhisperRepo 'build\bin'
    if (-not (Test-Path $WhisperBin)) {
        throw "whisper.cpp build output not found: $WhisperBin (cmake --build build needed first)"
    }

$CMakeCache = Join-Path $WhisperRepo 'build\CMakeCache.txt'
if (-not (Test-Path $CMakeCache)) {
    throw "whisper.cpp CMake cache not found: $CMakeCache"
}
$compilerMatch = Select-String -Path $CMakeCache `
    -Pattern '^CMAKE_CXX_COMPILER:(?:FILEPATH|STRING)=(.+)$' |
    Select-Object -First 1
if (-not $compilerMatch) {
    throw "CMAKE_CXX_COMPILER not found in $CMakeCache"
}
$CxxExe = $compilerMatch.Matches[0].Groups[1].Value.Trim()
if (-not (Test-Path -LiteralPath $CxxExe -PathType Leaf)) {
    throw "C++ compiler recorded by whisper.cpp no longer exists: $CxxExe"
}
$MingwBin = Split-Path -Parent $CxxExe
foreach ($name in $MingwDlls) {
    if (-not (Test-Path -LiteralPath (Join-Path $MingwBin $name) -PathType Leaf)) {
        throw "MinGW runtime paired with the whisper.cpp build is missing: $(Join-Path $MingwBin $name)"
    }
}

if (-not $OutDir) {
    $OutDir = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-native-$Version"
}
if (Test-Path $OutDir) {
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "OutDir exists, cleaning: $OutDir" -Role Warning
    Remove-Item $OutDir -Recurse -Force
}
$null = New-Item -ItemType Directory -Path $OutDir

$ZipName    = "deckle-native-$Version.zip"
$ZipPath    = Join-Path $OutDir $ZipName
$StagingDir = Join-Path $OutDir 'staging'
$null = New-Item -ItemType Directory -Path $StagingDir

# ── Stage DLLs + compute per-file SHA256 ─────────────────────────────────────

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Stage DLLs to $StagingDir"

$Sha256ByName = [ordered]@{}
function Stage-Dll($srcDir, $name) {
    $src = Join-Path $srcDir $name
    if (-not (Test-Path $src)) { throw "MISSING source $src" }
    $dst = Join-Path $StagingDir $name
    Copy-Item $src $dst
    $hash = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLower()
    $Sha256ByName[$name] = $hash
    $size = [math]::Round((Get-Item $dst).Length / 1MB, 2)
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message ("{0,-22} {1,8} MB  sha256={2}" -f $name, $size, $hash)
}

foreach ($n in $WhisperDlls) { Stage-Dll $WhisperBin  $n }
foreach ($n in $MingwDlls)   { Stage-Dll $MingwBin $n }

# ── Gather provenance metadata ───────────────────────────────────────────────

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Gather provenance'

# whisper.cpp upstream version — read from CMakeLists.txt `project(... VERSION X.Y.Z)`.
$cmakeLists = Join-Path $WhisperRepo 'CMakeLists.txt'
$WhisperVersion = 'unknown'
if (Test-Path $cmakeLists) {
    $match = (Select-String -Path $cmakeLists -Pattern 'project\([^)]*VERSION\s+([^\s)]+)' | Select-Object -First 1)
    if ($match) { $WhisperVersion = $match.Matches[0].Groups[1].Value }
}

# whisper.cpp commit — only available when WhisperRepo is an actual git clone.
$WhisperCommit = 'unknown (source extraction, not a git clone)'
if (Test-Path (Join-Path $WhisperRepo '.git')) {
    $rev = & git -C $WhisperRepo rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $rev) { $WhisperCommit = $rev.Trim() }
}

# Compiler — query the exact executable CMake used for this build.
$CompilerLine = 'unknown'
if (Test-Path $CxxExe) {
    $line = (& $CxxExe --version 2>$null | Select-Object -First 1)
    if ($line) { $CompilerLine = $line }
}

# Vulkan SDK version — Scoop installs as <root>\apps\vulkan\<version>\, so the
# resolved 'current' shim points at the version dir; reading the symlink
# target is fragile, so we just read the version line from sdk_version.txt
# if present, else fall back to the env var path tail.
$VulkanSdk = if ($env:VULKAN_SDK) {
    $verFile = Join-Path $env:VULKAN_SDK 'sdk_version.txt'
    if (Test-Path $verFile) {
        (Get-Content $verFile -First 1).Trim()
    } else {
        # Resolve symlink target if any; otherwise print the literal path tail.
        $resolved = (Get-Item $env:VULKAN_SDK -ErrorAction SilentlyContinue).Target
        if ($resolved) { Split-Path -Leaf $resolved } else { Split-Path -Leaf $env:VULKAN_SDK }
    }
} else { 'unknown (VULKAN_SDK not set)' }

# CMake version
$CMakeLine = 'unknown'
$cmakeOut = (& cmake --version 2>$null | Select-Object -First 1)
if ($cmakeOut) { $CMakeLine = ($cmakeOut -replace '^cmake version\s+', '') }

# Ninja version
$NinjaLine = 'unknown'
$ninjaOut = (& ninja --version 2>$null)
if ($ninjaOut) { $NinjaLine = $ninjaOut.Trim() }

# Build flags from CMakeCache
$VulkanFlags = @()
if (Test-Path $CMakeCache) {
    $VulkanFlags = (Select-String -Path $CMakeCache -Pattern '^GGML_VULKAN[A-Z_]*:BOOL=' |
                    ForEach-Object { $_.Line })
}

$Hostname  = $env:COMPUTERNAME
$BuildDate = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')

# ── Compose PROVENANCE.txt ───────────────────────────────────────────────────

$filesBlock = ''
foreach ($n in ($WhisperDlls + $MingwDlls)) {
    $filesBlock += "{0,-22} sha256={1}`n" -f $n, $Sha256ByName[$n]
}

$prov = @"
Deckle native runtime bundle
============================

Bundle version : native-v$Version
Build date     : $BuildDate
Builder        : $Hostname

Upstream
--------
whisper.cpp    : v$WhisperVersion
commit         : $WhisperCommit

Toolchain
---------
Compiler       : $CompilerLine
Runtime DLLs   : $MingwBin
Vulkan SDK     : $VulkanSdk
CMake          : $CMakeLine
Generator      : Ninja $NinjaLine

Build flags
-----------
$($VulkanFlags -join "`n")

Files (8)
---------
$filesBlock

Licenses
--------
whisper.cpp / ggml : MIT — https://github.com/ggerganov/whisper.cpp/blob/master/LICENSE
MinGW C++ runtime  : GPL-3 with runtime exception (libgcc, libstdc++) /
                     MIT (libwinpthread). Redistribution permitted as
                     dynamic linkage runtime per the GCC runtime library
                     exception.

Reproduction
------------
Built by scripts/commands/publish-native-runtime.ps1 from the local
whisper.cpp build and MinGW runtime directories recorded above.
"@

Set-Content -Path (Join-Path $StagingDir 'PROVENANCE.txt') -Value $prov -Encoding UTF8
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'PROVENANCE.txt written'

# ── SHA256SUMS (sha256sum -c compatible) ─────────────────────────────────────

$sumsLines = foreach ($n in ($WhisperDlls + $MingwDlls)) {
    '{0} *{1}' -f $Sha256ByName[$n], $n
}
Set-Content -Path (Join-Path $StagingDir 'SHA256SUMS') -Value $sumsLines -Encoding UTF8
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'SHA256SUMS written'

# ── Zip flat + zip-level SHA256 ──────────────────────────────────────────────

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Compress to $ZipName"
Compress-Archive -Path (Join-Path $StagingDir '*') -DestinationPath $ZipPath -Force
Assert-DeckleNativeRuntimeArchive `
    -ArchivePath $ZipPath `
    -DllNames $NativeRuntimeCatalog.Names
$ZipSha256 = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$ZipBytes  = (Get-Item $ZipPath).Length
$ZipSize   = [math]::Round($ZipBytes / 1MB, 2)
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "$ZipName ($ZipSize MB) sha256=$ZipSha256"

    Remove-Item $StagingDir -Recurse -Force
    $SourceKind = 'whisper.cpp build'
}

# ── Summary — paste-ready block for NativeRuntime.CurrentBundle ──────────────

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Done'
Write-Host @"

  Zip    : $ZipPath
  Size   : $ZipBytes bytes ($ZipSize MB)
  SHA256 : $ZipSha256
"@ -ForegroundColor Green

if (-not $ArtifactPath) {
    Write-Host @"
  Paste into src/Deckle.Transcription.Whisper/Setup/NativeRuntime.cs CurrentBundle
  (URL resolved from the selected Deckle repository):

    public static NativeRuntimeBundle CurrentBundle { get; } = new(
        Version:     "$Version",
        Url:         "https://github.com/$OwnerRepo/releases/download/native-v$Version/$ZipName",
        Sha256:      "$ZipSha256",
        SizeBytes:   ${ZipBytes}L,
        DisplayName: "Whisper.cpp + Vulkan runtime");

"@ -ForegroundColor Green
}

# ── Optional: explicit draft → verify → tag → publish ────────────────────────

if ($Publish) {
    $tag = "native-v$Version"
    $title = "Native runtime $tag"
    if (-not $Notes) {
        $Notes = Join-Path $OutDir 'release-notes.md'
        @(
            "Deckle native runtime $Version"
            ''
            'Prebuilt whisper.cpp and MinGW runtime libraries for Deckle first-run setup.'
            ''
            "SHA-256: ``$ZipSha256``"
        ) | Set-Content -LiteralPath $Notes -Encoding utf8
    } elseif (-not (Test-Path -LiteralPath $Notes -PathType Leaf)) {
        throw "Release notes file not found: $Notes"
    }

    Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Publish GitHub Release $tag"
    $releaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish 2>$null) -join "`n"
    $releaseExists = $LASTEXITCODE -eq 0
    if (-not $releaseExists) {
        & gh release create $tag $ZipPath `
            --repo $OwnerRepo `
            --title $title `
            --target $HeadSha `
            --notes-file $Notes `
            --draft
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed (code $LASTEXITCODE)" }
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Draft release $tag uploaded"
        $releaseJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish) -join "`n"
        if ($LASTEXITCODE -ne 0) { throw "GitHub draft $tag could not be read after upload" }
    } else {
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Existing release $tag found; validating it for resume"
    }

    $remoteRelease = $releaseJson | ConvertFrom-Json
    Assert-DeckleReleaseAssets `
        -Release $remoteRelease `
        -Tag $tag `
        -HeadSha $HeadSha `
        -ExpectedNames @($ZipName) `
        -ExpectedSizes @{ $ZipName = $ZipBytes }
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'GitHub asset verified by name and byte size'

    $downloadDir = Join-Path ([IO.Path]::GetTempPath()) "deckle-native-remote-$([guid]::NewGuid())"
    try {
        $null = New-Item -ItemType Directory -Path $downloadDir
        & gh release download $tag `
            --repo $OwnerRepo `
            --pattern $ZipName `
            --dir $downloadDir
        if ($LASTEXITCODE -ne 0) { throw "GitHub asset download failed (code $LASTEXITCODE)" }
        $downloadedArtifact = Join-Path $downloadDir $ZipName
        $releaseArtifact = [pscustomobject]@{
            Version   = $Version
            SizeBytes = $ZipBytes
            Sha256    = $ZipSha256
        }
        Assert-DeckleNativeRuntimeArtifact `
            -Bundle $releaseArtifact `
            -ArtifactPath $downloadedArtifact | Out-Null
        Assert-DeckleNativeRuntimeArchive `
            -ArchivePath $downloadedArtifact `
            -DllNames $NativeRuntimeCatalog.Names
        Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'GitHub asset downloaded and verified byte-for-byte'
    } finally {
        if (Test-Path -LiteralPath $downloadDir) {
            Remove-Item -LiteralPath $downloadDir -Recurse -Force
        }
    }

    Publish-DeckleReleaseTag -RepoRoot $RepoRoot -Tag $tag -HeadSha $HeadSha
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "GitHub tag $tag published at release HEAD"

    if ($remoteRelease.isDraft) {
        & gh release edit $tag --repo $OwnerRepo --draft=false
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub draft finalization failed (code $LASTEXITCODE); the verified draft remains hidden"
        }
    }
    $publishedJson = (& gh release view $tag --repo $OwnerRepo --json isDraft,assets,tagName,targetCommitish) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Published GitHub release $tag could not be read back" }
    $publishedRelease = $publishedJson | ConvertFrom-Json
    if ($publishedRelease.isDraft) { throw "GitHub release $tag is still a draft after finalization" }
    Assert-DeckleReleaseAssets -Release $publishedRelease -Tag $tag -HeadSha $HeadSha -ExpectedNames @($ZipName) -ExpectedSizes @{ $ZipName = $ZipBytes }
    $Published = $true
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Released as $tag"
}

$nativeTag = "native-v$Version"
$sentence = if ($Published) {
    "Native runtime $nativeTag was validated and published as a GitHub Release."
} elseif ($ArtifactPath) {
    "Native runtime $nativeTag was validated locally and is ready to publish."
} else {
    "Native runtime $nativeTag was packaged locally for inspection."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result Success `
    -Sentence $sentence `
    -Details ([ordered]@{
        Version     = $nativeTag
        DeckleRepo  = $RepoRoot
        Source      = $SourceKind
        WhisperRepo = $WhisperRepo
        Artifact    = $ArtifactPath
        OutDir      = $OutDir
        Zip         = $ZipPath
        Size        = $(if ($ZipBytes) { "$ZipBytes bytes ($ZipSize MB)" } else { $null })
        SHA256      = $ZipSha256
        Published   = $(if ($Published) { 'Yes' } else { 'No' })
    }) `
    -Next $(if (-not $Published) { @("Run again with -Publish to create the GitHub Release.") } else { @("Update NativeRuntime.CurrentBundle if the app should consume this bundle.") })
