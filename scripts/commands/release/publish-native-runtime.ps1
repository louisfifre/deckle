<#
.SYNOPSIS
    Prepare or publish the native runtime
.DESCRIPTION
    Packages a versioned zip of the native DLLs that Deckle's first-run wizard
    downloads at install time. The wizard fetches this zip from a GitHub
    Release of the Deckle repo and extracts it into the user data folder.
    With -Publish the zip is also published as a native-vX.Y.Z release via gh.
.NOTES
    section: Release
    order: 120
    action: Prepare native runtime |
    action: Publish native runtime | -Publish | danger | Publishes a public GitHub Release of the native runtime.
#>

# The zip stays flat: NativeRuntime.InstallFromZipAsync looks each DLL up by
# its bare name at the archive root.

[CmdletBinding()]
param(
    # Optional override for recovery. Normally inferred from whisper.cpp:
    # X.Y tracks its upstream minor, Z is the next local rebuild counter.
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    # Path to a local whisper.cpp clone with <WhisperBuildDir>/bin/ already
    # populated by `cmake --build`. Used only when packaging a new runtime
    # bundle.
    [string]$WhisperRepo,

    # Build folder under the whisper.cpp clone. The runtime is an MSVC build
    # with dynamic ggml backends (GGML_BACKEND_DL, GGML_CPU_ALL_VARIANTS,
    # GGML_VULKAN); the script refuses any other compiler.
    [string]$WhisperBuildDir = 'build',

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
$LibDir = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'pe-imports.psm1') -Force
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force
Import-Module (Join-Path $LibDir 'release-validation.psm1') -Force

$RepoRoot = if ($Target) {
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { throw "Target not found: $Target" }
    (Get-Item -LiteralPath $Target).FullName
} else {
    Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $ScriptDir))
}
$NativeRuntimeSource = Join-Path $RepoRoot 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
$NativeRuntimeCatalog = Get-DeckleNativeRuntimeCatalog -SourcePath $NativeRuntimeSource

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
    # A bundle that matched its hash can still be one that only loads on the
    # machine that built it: its import tables are checked every time.
    Assert-DeckleNativeRuntimeArchiveLoadable `
        -ArchivePath $ZipPath `
        -DllNames $NativeRuntimeCatalog.Names
    $ZipSha256 = $verifiedArtifact.Sha256
    $ZipBytes = $verifiedArtifact.SizeBytes
    $ZipSize = [math]::Round($ZipBytes / 1MB, 2)
    if (-not $OutDir) { $OutDir = Split-Path -Parent $ZipPath }
    $SourceKind = 'Existing artifact'
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "$ZipName ($ZipSize MB) matches CurrentBundle and the native catalog, and every import resolves inside the bundle or Windows"
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

    # The build tree names its compiler; the runtime DLLs come from the Redist
    # of that same Visual Studio, so the bundle never mixes toolsets.
    $Build = Get-DeckleNativeRuntimeBuild -WhisperRepo $WhisperRepo -BuildDir $WhisperBuildDir
    $DllSources = Resolve-DeckleNativeRuntimeDllSources -Build $Build -Catalog $NativeRuntimeCatalog
    $WhisperBin = $Build.Bin
    $CxxExe = $Build.Compiler
    $CMakeCache = $Build.CMakeCache

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

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message "Stage DLLs to $StagingDir"

$Sha256ByName = [ordered]@{}
foreach ($name in $NativeRuntimeCatalog.Names) {
    $src = $DllSources[$name]
    $dst = Join-Path $StagingDir $name
    Copy-Item -LiteralPath $src -Destination $dst
    $hash = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLower()
    $Sha256ByName[$name] = $hash
    $size = [math]::Round((Get-Item $dst).Length / 1MB, 2)
    Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message ("{0,-24} {1,8} MB  sha256={2}" -f $name, $size, $hash)
}

# Refuse the bundle here, before it is even zipped, when one of its DLLs
# imports something the tester's machine would not have: the build machine
# hides such a gap behind its PATH, a bare Windows does not.
Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Check every import resolves inside the bundle or Windows'
Assert-DeckleNativeImportsResolved `
    -Directory $StagingDir `
    -DllNames $NativeRuntimeCatalog.Names `
    -ExternalDllNames (Get-DeckleNativeExternalDllNames)
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message "Imports resolve; external: $((Get-DeckleNativeExternalDllNames) -join ', ')"

Write-DeckleWorkflowStep -Output $WorkflowOutput -Message 'Gather provenance'

$cmakeLists = Join-Path $WhisperRepo 'CMakeLists.txt'
$WhisperVersion = 'unknown'
if (Test-Path $cmakeLists) {
    $match = (Select-String -Path $cmakeLists -Pattern 'project\([^)]*VERSION\s+([^\s)]+)' | Select-Object -First 1)
    if ($match) { $WhisperVersion = $match.Matches[0].Groups[1].Value }
}

$WhisperCommit = 'unknown (source extraction, not a git clone)'
if (Test-Path (Join-Path $WhisperRepo '.git')) {
    $rev = & git -C $WhisperRepo rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $rev) { $WhisperCommit = $rev.Trim() }
}

# The exact compiler CMake used for this build, not whichever one PATH finds.
# Read from the file's version resource: cl.exe prints its banner only to
# stderr and needs the vcvars environment to run at all.
$CompilerLine = 'unknown'
$compilerInfo = (Get-Item -LiteralPath $CxxExe).VersionInfo
if ($compilerInfo.ProductVersion) {
    $CompilerLine = "MSVC cl.exe $($compilerInfo.ProductVersion) ($CxxExe)"
}

# Scoop installs the Vulkan SDK under apps\vulkan\<version>\ behind a 'current'
# link: sdk_version.txt is the reliable source, the link target's leaf only a
# fallback.
$VulkanSdk = if ($env:VULKAN_SDK) {
    $verFile = Join-Path $env:VULKAN_SDK 'sdk_version.txt'
    if (Test-Path $verFile) {
        (Get-Content $verFile -First 1).Trim()
    } else {
        $resolved = (Get-Item $env:VULKAN_SDK -ErrorAction SilentlyContinue).Target
        if ($resolved) { Split-Path -Leaf $resolved } else { Split-Path -Leaf $env:VULKAN_SDK }
    }
} else { 'unknown (VULKAN_SDK not set)' }

$CMakeLine = 'unknown'
$cmakeOut = (& cmake --version 2>$null | Select-Object -First 1)
if ($cmakeOut) { $CMakeLine = ($cmakeOut -replace '^cmake version\s+', '') }

$NinjaLine = 'unknown'
$ninjaOut = (& ninja --version 2>$null)
if ($ninjaOut) { $NinjaLine = $ninjaOut.Trim() }

$BuildFlags = @($Build.BuildFlags)

$Hostname  = $env:COMPUTERNAME
$BuildDate = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')

$filesBlock = ''
foreach ($n in $NativeRuntimeCatalog.Names) {
    $filesBlock += "{0,-24} sha256={1}`n" -f $n, $Sha256ByName[$n]
}
$fileCount = $NativeRuntimeCatalog.Names.Count

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
Runtime DLLs   : Visual C++ Redistributable $($Build.RedistVersion)
                 $($Build.RedistDirs -join "`n                 ")
Vulkan SDK     : $VulkanSdk
CMake          : $CMakeLine
Generator      : Ninja $NinjaLine

Build flags
-----------
$($BuildFlags -join "`n")

External dependencies
---------------------
$((Get-DeckleNativeExternalDllNames) -join "`n") (provided by the graphics driver; without it the CPU backend is used)

Files ($fileCount)
----------
$filesBlock

Licenses
--------
whisper.cpp / ggml : MIT — https://github.com/ggerganov/whisper.cpp/blob/master/LICENSE
Visual C++ runtime : Microsoft Visual C++ Redistributable (vcruntime140,
                     vcruntime140_1, msvcp140, vcomp140). Redistributable
                     application-locally under the Visual Studio license
                     terms (Distributable Code, REDIST.TXT).

Reproduction
------------
Built by scripts/commands/release/publish-native-runtime.ps1 from the local
whisper.cpp build and the Visual C++ Redist folders recorded above.
"@

Set-Content -Path (Join-Path $StagingDir 'PROVENANCE.txt') -Value $prov -Encoding UTF8
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'PROVENANCE.txt written'

# SHA256SUMS stays `sha256sum -c` compatible.
$sumsLines = foreach ($n in $NativeRuntimeCatalog.Names) {
    '{0} *{1}' -f $Sha256ByName[$n], $n
}
Set-Content -Path (Join-Path $StagingDir 'SHA256SUMS') -Value $sumsLines -Encoding UTF8
Write-DeckleWorkflowMessage -Output $WorkflowOutput -Message 'SHA256SUMS written'

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

# Draft, verify the uploaded bytes, tag, then publish: a broken asset never
# becomes public.
if ($Publish) {
    $tag = "native-v$Version"
    $title = "Native runtime $tag"
    if (-not $Notes) {
        $Notes = Join-Path $OutDir 'release-notes.md'
        @(
            "Deckle native runtime $Version"
            ''
            'Prebuilt whisper.cpp (MSVC, dynamic ggml backends) and Visual C++ runtime libraries for Deckle first-run setup.'
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
        Assert-DeckleNativeRuntimeArchiveLoadable `
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

    # Always refuse the repo's "Latest" badge: it belongs to the app's vX.Y.Z
    # releases, and GitHub's automatic rule is not trusted to order native-v*
    # tags below them. The edit is run even on a resumed, already public
    # release so the badge is set either way.
    & gh release edit $tag --repo $OwnerRepo --draft=false --latest=false
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release finalization failed (code $LASTEXITCODE); the verified release may remain hidden"
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
