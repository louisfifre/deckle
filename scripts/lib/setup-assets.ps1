# setup-assets.ps1
#
# Populates <UserDataRoot>\native\ and <UserDataRoot>\models\ with the
# whisper.cpp DLLs, MinGW C++ runtime, and Whisper models the app needs
# at runtime. This is the canonical setup step — the app reads from
# <UserDataRoot> exclusively (see src/Deckle.Core/Paths/AppPaths.cs).
#
# Default <UserDataRoot> resolution mirrors AppPaths.ResolveUserDataRoot
# on the C# side:
#   1. -DataRoot parameter (highest priority)
#   2. DECKLE_DATA_ROOT environment variable
#   3. %LOCALAPPDATA%\Deckle\
#
# Native runtime sources (mutually exclusive — exactly one wins):
#   1. -FromRelease <X.Y.Z> —
#      downloads deckle-native-<X.Y.Z>.zip from the Deckle GitHub Release
#      and extracts the catalog DLLs in place. No local whisper.cpp clone
#      needed.
#   2. -WhisperRepo <path> — copies DLLs from a local whisper.cpp build
#      tree (path must contain build\bin\). For developers who recompile
#      whisper.cpp themselves with the Vulkan backend.
#   3. Neither passed — falls back to %DECKLE_WHISPER_REPO% if set, else
#      <repo>\..\whisper.cpp (sibling clone). Skipped with a warning if
#      no source resolves — useful when only models need refreshing.
#
# Models sources:
#   - HuggingFace via curl.exe (built into Win10/11, fast for large files,
#     native progress bar, idempotent on file size).
#
# Idempotent: skips files already present at the same size unless -Force.

[CmdletBinding(DefaultParameterSetName = 'WhisperRepo')]
param(
    # Override of the target UserDataRoot. Highest priority, ahead of
    # DECKLE_DATA_ROOT and %LOCALAPPDATA%\Deckle\.
    [string]$DataRoot,

    # Native runtime — fetch the published bundle from the Deckle GitHub
    # Release tagged native-v<Version>. Default for non-rebuilders.
    # Mutually exclusive with -WhisperRepo.
    [Parameter(ParameterSetName = 'FromRelease')]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$FromRelease,

    # Native runtime — copy DLLs from a local whisper.cpp build tree.
    # Path must contain build\bin\ (output of `cmake --build`). Mutually
    # exclusive with -FromRelease.
    [Parameter(ParameterSetName = 'WhisperRepo')]
    [string]$WhisperRepo,

    # Download ggml-large-v3.bin (~3 GB) on top of ggml-base.bin. Off by
    # default to keep first-time setup fast.
    [switch]$WithLarge,

    # Stage the CamemBERT reranker model (~440 MB ONNX) under
    # models\camembert-base. Off by default — not yet consumed by the live
    # engine; fetched on demand and referenced from one canonical location.
    [switch]$WithCamembert,

    # Re-copy / re-download even if the destination already exists with
    # the matching size.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
. (Join-Path $ScriptDir 'action-summary.ps1')
Import-Module (Join-Path $ScriptDir 'native-runtime-release.psm1') -Force

# Repo paths — two levels up: scripts/lib → scripts → repo root
$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Scoop respects the SCOOP env var when installed in a non-default location;
# fall back to the per-user default %USERPROFILE%\scoop otherwise.
$ScoopRoot   = if ($env:SCOOP) { $env:SCOOP } else { Join-Path $env:USERPROFILE 'scoop' }
$ScoopMingw  = Join-Path $ScoopRoot 'apps\mingw\current\bin'

# Target root resolution — same order as AppPaths.ResolveUserDataRoot
if ($DataRoot)                 { $TargetRoot = $DataRoot }
elseif ($env:DECKLE_DATA_ROOT) { $TargetRoot = $env:DECKLE_DATA_ROOT }
else                           { $TargetRoot = Join-Path $env:LOCALAPPDATA 'Deckle' }

$TargetNative = Join-Path $TargetRoot 'native'
$TargetModels = Join-Path $TargetRoot 'models'

function Step($msg) { Write-Host "`n[setup] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "         $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "         $msg" -ForegroundColor Yellow }

$Workflow = 'Set up runtime assets'
$NativeSource = $null
$NativeRuntimeStatus = 'Pending'
$ModelStatus = 'Pending'

trap {
    Write-DeckleActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Runtime asset setup failed before completion." `
        -Details ([ordered]@{
            'Data root'      = $TargetRoot
            Native          = $NativeRuntimeStatus
            'Native source' = $NativeSource
            Models          = $ModelStatus
            Error           = $_.Exception.Message
        })
    throw
}

# NativeRuntime.cs owns the catalog consumed by the app, local setup, and the
# release packager. Reading it here makes a dependency change one edit instead
# of a three-file synchronization convention.
$NativeRuntimeSource = Join-Path $Repo 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
$NativeRuntimeCatalog = Get-DeckleNativeRuntimeCatalog -SourcePath $NativeRuntimeSource
$WhisperDlls = @($NativeRuntimeCatalog.WhisperDlls)
$MingwDlls = @($NativeRuntimeCatalog.MingwDlls)

# Hosting source for the published native runtime bundle. Must match
# NativeRuntime.CurrentBundle.Url on the C# side.
$DeckleRepoSlug = 'louisfifre/deckle'

# Idempotent copy — skip if destination already exists with same size,
# unless -Force was passed. Cheaper than a hash check and good enough
# for a setup script (the only mutation source is this script itself).
function CopyIdempotent($src, $dst) {
    $name = Split-Path $src -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -eq (Get-Item $src).Length)) {
        Ok "skip  $name (same size)"
        return
    }
    Copy-Item $src $dst -Force
    $size = [math]::Round((Get-Item $dst).Length / 1MB, 1)
    Ok "copy  $name ($size MB)"
}

# Idempotent download via curl.exe (built into Win10/11, much faster than
# Invoke-WebRequest for large files, native progress bar). $expectedMinBytes
# is a coarse "is the file present and roughly complete" check.
function Download($url, $dst, $expectedMinBytes) {
    $name = Split-Path $dst -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -ge $expectedMinBytes)) {
        Ok "already present $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB)"
        return
    }
    Write-Host "         downloading $name ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $dst $url
    $curlExitCode = $LASTEXITCODE
    Write-Host ''
    if ($curlExitCode -ne 0) { throw "curl failed for $url" }
    Ok "downloaded $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB)"
}

Step "target root: $TargetRoot"

# 1. Create target folders.
Step 'create folders'
foreach ($d in @($TargetNative, $TargetModels)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Ok "created $d"
    } else {
        Ok "exists  $d"
    }
}

# 2. Native runtime — three modes (release / local-rebuild / skip).
Step 'native runtime'

if ($FromRelease) {
    # Mode A — pull the published bundle from the Deckle GitHub Release.
    # Trust HTTPS for integrity here; the wizard side enforces SHA-256
    # against the catalog in NativeRuntime.CurrentBundle, but this script
    # is a dev convenience and doesn't ship a hash table.
    $url = "https://github.com/$DeckleRepoSlug/releases/download/native-v$FromRelease/deckle-native-$FromRelease.zip"
    $tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-native-$FromRelease.zip"
    $NativeSource = "GitHub Release native-v$FromRelease"

    if ($Force -and (Test-Path $tmpZip)) { Remove-Item $tmpZip -Force }
    Ok "url $url"
    & curl.exe -L --fail --retry 3 --progress-bar -o $tmpZip $url
    $curlExitCode = $LASTEXITCODE
    Write-Host ''
    if ($curlExitCode -ne 0) { throw "curl failed for $url" }
    Ok "downloaded $(Split-Path $tmpZip -Leaf) ($([math]::Round((Get-Item $tmpZip).Length/1MB,1)) MB)"

    # Extract only the catalog entries — same defense-in-depth filter the
    # C# side applies in NativeRuntime.InstallFromZipAsync.
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $catalog = $WhisperDlls + $MingwDlls
    $archive = [System.IO.Compression.ZipFile]::OpenRead($tmpZip)
    try {
        foreach ($name in $catalog) {
            $entry = $archive.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if (-not $entry) {
                Warn "MISSING $name in bundle"
                continue
            }
            $dst = Join-Path $TargetNative $name
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dst, $true)
            $size = [math]::Round((Get-Item $dst).Length / 1MB, 1)
            Ok "extract $name ($size MB)"
        }
    } finally {
        $archive.Dispose()
    }
    Remove-Item $tmpZip -Force
    $NativeRuntimeStatus = "Extracted from $NativeSource"
}
else {
    # Modes B + C — local whisper.cpp build tree. -WhisperRepo wins,
    # then $env:DECKLE_WHISPER_REPO, then a sibling <repo>\..\whisper.cpp
    # clone, then we skip with a warning (useful when only models need
    # refreshing on a machine without a build tree).
    if ($WhisperRepo)                  { $resolved = $WhisperRepo }
    elseif ($env:DECKLE_WHISPER_REPO)  { $resolved = $env:DECKLE_WHISPER_REPO }
    else                               { $resolved = Join-Path (Split-Path $Repo -Parent) 'whisper.cpp' }

    $whisperBin = Join-Path $resolved 'build\bin'
    if (Test-Path $whisperBin) {
        $NativeSource = $whisperBin
        Ok "whisper.cpp build : $whisperBin"
        foreach ($name in $WhisperDlls) {
            $src = Join-Path $whisperBin $name
            if (-not (Test-Path $src)) {
                Warn "MISSING source $src — rebuild whisper.cpp needed"
                continue
            }
            CopyIdempotent $src (Join-Path $TargetNative $name)
        }

        # MinGW runtime DLLs come from the Scoop install, paired with the
        # toolchain that produced ggml-vulkan.dll above.
        if (Test-Path $ScoopMingw) {
            Ok "mingw runtime    : $ScoopMingw"
            foreach ($name in $MingwDlls) {
                $src = Join-Path $ScoopMingw $name
                if (-not (Test-Path $src)) {
                    Warn "MISSING source $src — install MinGW via Scoop"
                    continue
                }
                CopyIdempotent $src (Join-Path $TargetNative $name)
            }
        } else {
            Warn "MinGW Scoop install not found at $ScoopMingw — runtime DLLs skipped"
            $NativeRuntimeStatus = 'Partial: whisper.cpp DLLs copied, MinGW runtime missing'
        }
        if ($NativeRuntimeStatus -eq 'Pending') {
            $NativeRuntimeStatus = "Copied from $NativeSource"
        }
    } else {
        $NativeSource = $resolved
        $NativeRuntimeStatus = 'Skipped: no native source found'
        Warn "no native source found"
        Warn "  tried -WhisperRepo, DECKLE_WHISPER_REPO, $resolved"
        Warn "  pass -FromRelease <X.Y.Z> to fetch the published bundle, or"
        Warn "  point -WhisperRepo at your whisper.cpp build tree"
    }
}

# 3. Whisper models — downloaded from HuggingFace into <TargetRoot>\models\.
# ggml-base.bin is the fast default, ggml-large-v3.bin is gated behind
# -WithLarge because it's 3 GB. Silero VAD is always fetched — it's tiny.
Step 'download Whisper models'
$baseDst = Join-Path $TargetModels 'ggml-base.bin'
Download `
    'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' `
    $baseDst `
    130MB

if ($WithLarge) {
    $largeDst = Join-Path $TargetModels 'ggml-large-v3.bin'
    Download `
        'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin' `
        $largeDst `
        3000MB
} else {
    Ok 'skipped ggml-large-v3.bin (pass -WithLarge to fetch ~3 GB model)'
}

Step 'download Silero VAD'
$vadDst = Join-Path $TargetModels 'silero_vad.onnx'
Download `
    'https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx' `
    $vadDst `
    2200KB
$ModelStatus = if ($WithLarge) { 'Base, large, and Silero VAD present' } else { 'Base and Silero VAD present; large skipped' }

# 4. CamemBERT masked-LM (ONNX) — the autocorrect post-sentence reranker's model,
# fetched from the Xenova ONNX export into <TargetRoot>\models\camembert-base\.
# The three files CamembertMlmScorer loads (model.onnx + tokenizer.json +
# sentencepiece.bpe.model) sit flat in that one directory, alongside the small
# config jsons. Gated behind -WithCamembert (~440 MB): the live engine does not
# consume it yet, so it is staged on demand from one canonical location the
# installer can reference, not pulled on every setup.
if ($WithCamembert) {
    Step 'download CamemBERT reranker model'
    $camDir = Join-Path $TargetModels 'camembert-base'
    if (-not (Test-Path $camDir)) { New-Item -ItemType Directory -Path $camDir -Force | Out-Null }
    $camBase = 'https://huggingface.co/Xenova/camembert-base/resolve/main'
    Download "$camBase/onnx/model.onnx"         (Join-Path $camDir 'model.onnx')              400MB
    Download "$camBase/tokenizer.json"          (Join-Path $camDir 'tokenizer.json')          1MB
    Download "$camBase/sentencepiece.bpe.model" (Join-Path $camDir 'sentencepiece.bpe.model') 500KB
    Download "$camBase/config.json"             (Join-Path $camDir 'config.json')             100
    Download "$camBase/tokenizer_config.json"   (Join-Path $camDir 'tokenizer_config.json')   100
    Download "$camBase/special_tokens_map.json" (Join-Path $camDir 'special_tokens_map.json') 100
    Ok "camembert-base staged under $camDir"
} else {
    Ok 'skipped camembert-base (pass -WithCamembert to fetch ~440 MB reranker model)'
}

# Final summary.
$nativeCount = (Get-ChildItem $TargetNative -File -ErrorAction SilentlyContinue | Measure-Object).Count
$modelCount  = (Get-ChildItem $TargetModels -File -ErrorAction SilentlyContinue | Measure-Object).Count
$nativeCatalogCount = @(($WhisperDlls + $MingwDlls) | Where-Object { Test-Path (Join-Path $TargetNative $_) }).Count
Step 'done'
Write-Host "         $TargetNative : $nativeCount file(s)"
Write-Host "         $TargetModels : $modelCount file(s)"
Write-Host "`nNext: scripts\deckle.ps1 (Build & run)" -ForegroundColor Cyan

$summaryResult = if ($nativeCatalogCount -lt ($WhisperDlls.Count + $MingwDlls.Count)) { 'Partial' } else { 'Success' }
$summarySentence = if ($summaryResult -eq 'Partial') {
    "Runtime asset setup finished, but the native runtime catalog is incomplete."
} else {
    "Runtime assets were set up under $TargetRoot."
}

Write-DeckleActionSummary `
    -Workflow $Workflow `
    -Result $summaryResult `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        'Data root'      = $TargetRoot
        Native          = $NativeRuntimeStatus
        'Native catalog' = "$nativeCatalogCount / $($WhisperDlls.Count + $MingwDlls.Count)"
        'Native files'  = $nativeCount
        Models          = $ModelStatus
        'Model files'   = $modelCount
        Force           = $(if ($Force) { 'Yes' } else { 'No' })
    }) `
    -Next @("Run scripts\deckle.ps1 and choose Build & run.")
