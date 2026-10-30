<#
.SYNOPSIS
    Set up runtime assets
.DESCRIPTION
    Populates the native and models folders under the user data root with the
    whisper.cpp DLLs, the Visual C++ runtime they import and the Whisper
    models the app needs at runtime. Idempotent: skips files already present
    at the same size unless -Force.
.NOTES
    section: Machine
    order: 120
#>

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

    # Native runtime — copy DLLs from a local whisper.cpp build tree built
    # with MSVC; the Visual C++ runtime comes from the same Visual Studio.
    # Mutually exclusive with -FromRelease.
    [Parameter(ParameterSetName = 'WhisperRepo')]
    [string]$WhisperRepo,

    # Build folder under the whisper.cpp repo (output of `cmake --build`).
    [Parameter(ParameterSetName = 'WhisperRepo')]
    [string]$WhisperBuildDir = 'build',

    # Download ggml-large-v3.bin (~3 GB) on top of ggml-base.bin. Off by
    # default to keep first-time setup fast.
    [switch]$WithLarge,

    # Stage the CamemBERT reranker model (~440 MB ONNX) under
    # models\camembert-base. Off by default: the live engine does not load it.
    [switch]$WithCamembert,

    # Re-copy / re-download even if the destination already exists with
    # the matching size.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
Import-Module (Join-Path $LibDir 'native-runtime-release.psm1') -Force

$Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# Same order as AppPaths.ResolveUserDataRoot, after the -DataRoot override.
if ($DataRoot)                 { $TargetRoot = $DataRoot }
elseif ($env:DECKLE_DATA_ROOT) { $TargetRoot = $env:DECKLE_DATA_ROOT }
else                           { $TargetRoot = Join-Path $env:LOCALAPPDATA 'Deckle' }

$TargetNative = Join-Path $TargetRoot 'native'
$TargetModels = Join-Path $TargetRoot 'models'

$WorkflowOutput = New-WorkflowOutput -Category 'setup'

$Workflow = 'Set up runtime assets'
$NativeSource = $null
$NativeRuntimeStatus = 'Pending'
$ModelStatus = 'Pending'

trap {
    Write-ActionSummary `
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

# NativeRuntime.cs owns the catalog the app, this script and the release
# packager consume, so a dependency change stays one edit.
$NativeRuntimeSource = Join-Path $Repo 'src\Deckle.Transcription.Whisper\Setup\NativeRuntime.cs'
$NativeRuntimeCatalog = Get-DeckleNativeRuntimeCatalog -SourcePath $NativeRuntimeSource
$CatalogDlls = @($NativeRuntimeCatalog.Names)

# Must match NativeRuntime.CurrentBundle.Url.
$DeckleRepoSlug = 'louisfifre/deckle'

# Same size counts as present: cheaper than a hash, and enough for a dev setup
# script.
function CopyIdempotent($src, $dst) {
    $name = Split-Path $src -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -eq (Get-Item $src).Length)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "skip  $name (same size)"
        return
    }
    Copy-Item $src $dst -Force
    $size = [math]::Round((Get-Item $dst).Length / 1MB, 1)
    Write-WorkflowMessage -Output $WorkflowOutput -Message "copy  $name ($size MB)"
}

# curl.exe ships with Windows and beats Invoke-WebRequest on large files.
# $expectedMinBytes is a coarse "present and roughly complete" check.
function Download($url, $dst, $expectedMinBytes) {
    $name = Split-Path $dst -Leaf
    if (-not $Force -and (Test-Path $dst) -and ((Get-Item $dst).Length -ge $expectedMinBytes)) {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "already present $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB)"
        return
    }
    Write-Host "         downloading $name ..."
    & curl.exe -L --fail --retry 3 --progress-bar -o $dst $url
    $curlExitCode = $LASTEXITCODE
    Write-Host ''
    if ($curlExitCode -ne 0) { throw "curl failed for $url" }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloaded $name ($([math]::Round((Get-Item $dst).Length/1MB,1)) MB)"
}

Write-WorkflowStep -Output $WorkflowOutput -Message "target root: $TargetRoot"

Write-WorkflowStep -Output $WorkflowOutput -Message 'create folders'
foreach ($d in @($TargetNative, $TargetModels)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Write-WorkflowMessage -Output $WorkflowOutput -Message "created $d"
    } else {
        Write-WorkflowMessage -Output $WorkflowOutput -Message "exists  $d"
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'native runtime'

if ($FromRelease) {
    # Integrity rests on HTTPS here: the wizard checks SHA-256 against
    # NativeRuntime.CurrentBundle, this dev convenience carries no hash.
    $url = "https://github.com/$DeckleRepoSlug/releases/download/native-v$FromRelease/deckle-native-$FromRelease.zip"
    $tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) "deckle-native-$FromRelease.zip"
    $NativeSource = "GitHub Release native-v$FromRelease"

    if ($Force -and (Test-Path $tmpZip)) { Remove-Item $tmpZip -Force }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "url $url"
    & curl.exe -L --fail --retry 3 --progress-bar -o $tmpZip $url
    $curlExitCode = $LASTEXITCODE
    Write-Host ''
    if ($curlExitCode -ne 0) { throw "curl failed for $url" }
    Write-WorkflowMessage -Output $WorkflowOutput -Message "downloaded $(Split-Path $tmpZip -Leaf) ($([math]::Round((Get-Item $tmpZip).Length/1MB,1)) MB)"

    # Extract only the catalog entries — same defense-in-depth filter the
    # C# side applies in NativeRuntime.InstallFromZipAsync.
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($tmpZip)
    try {
        foreach ($name in $CatalogDlls) {
            $entry = $archive.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if (-not $entry) {
                Write-WorkflowMessage -Output $WorkflowOutput -Message "MISSING $name in bundle" -Role Warning
                continue
            }
            $dst = Join-Path $TargetNative $name
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dst, $true)
            $size = [math]::Round((Get-Item $dst).Length / 1MB, 1)
            Write-WorkflowMessage -Output $WorkflowOutput -Message "extract $name ($size MB)"
        }
    } finally {
        $archive.Dispose()
    }
    Remove-Item $tmpZip -Force
    $NativeRuntimeStatus = "Extracted from $NativeSource"
}
else {
    # Without a build tree the native step is skipped with a warning, which
    # suits a models-only refresh.
    if ($WhisperRepo)                  { $resolved = $WhisperRepo }
    elseif ($env:DECKLE_WHISPER_REPO)  { $resolved = $env:DECKLE_WHISPER_REPO }
    else                               { $resolved = Join-Path (Split-Path $Repo -Parent) 'whisper.cpp' }

    $whisperBin = Join-Path (Join-Path $resolved $WhisperBuildDir) 'bin'
    if (Test-Path $whisperBin) {
        # The same resolution the release packager uses: whisper.cpp output
        # from the build tree, the Visual C++ runtime from the Visual Studio
        # that compiled it. A MinGW tree or an incomplete build is refused
        # with its reason rather than copied in part.
        $build = Get-DeckleNativeRuntimeBuild -WhisperRepo $resolved -BuildDir $WhisperBuildDir
        $NativeSource = $build.Bin
        Write-WorkflowMessage -Output $WorkflowOutput -Message "whisper.cpp build : $($build.Bin)"
        Write-WorkflowMessage -Output $WorkflowOutput -Message "VC++ runtime      : Redist $($build.RedistVersion)"
        $sources = Resolve-DeckleNativeRuntimeDllSources -Build $build -Catalog $NativeRuntimeCatalog
        foreach ($name in $CatalogDlls) {
            CopyIdempotent $sources[$name] (Join-Path $TargetNative $name)
        }
        $NativeRuntimeStatus = "Copied from $NativeSource"
    } else {
        $NativeSource = $resolved
        $NativeRuntimeStatus = 'Skipped: no native source found'
        Write-WorkflowMessage -Output $WorkflowOutput -Message "no native source found" -Role Warning
        Write-WorkflowMessage -Output $WorkflowOutput -Message "  tried -WhisperRepo, DECKLE_WHISPER_REPO, $resolved" -Role Warning
        Write-WorkflowMessage -Output $WorkflowOutput -Message "  pass -FromRelease <X.Y.Z> to fetch the published bundle, or" -Role Warning
        Write-WorkflowMessage -Output $WorkflowOutput -Message "  point -WhisperRepo at your whisper.cpp build tree" -Role Warning
    }
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'download Whisper models'
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
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'skipped ggml-large-v3.bin (pass -WithLarge to fetch ~3 GB model)'
}

Write-WorkflowStep -Output $WorkflowOutput -Message 'download Silero VAD'
$vadDst = Join-Path $TargetModels 'silero_vad.onnx'
Download `
    'https://raw.githubusercontent.com/snakers4/silero-vad/v6.2/src/silero_vad/data/silero_vad.onnx' `
    $vadDst `
    2200KB
$ModelStatus = if ($WithLarge) { 'Base, large, and Silero VAD present' } else { 'Base and Silero VAD present; large skipped' }

# CamembertMlmScorer loads model.onnx, tokenizer.json and
# sentencepiece.bpe.model from one flat directory.
if ($WithCamembert) {
    Write-WorkflowStep -Output $WorkflowOutput -Message 'download CamemBERT reranker model'
    $camDir = Join-Path $TargetModels 'camembert-base'
    if (-not (Test-Path $camDir)) { New-Item -ItemType Directory -Path $camDir -Force | Out-Null }
    $camBase = 'https://huggingface.co/Xenova/camembert-base/resolve/main'
    Download "$camBase/onnx/model.onnx"         (Join-Path $camDir 'model.onnx')              400MB
    Download "$camBase/tokenizer.json"          (Join-Path $camDir 'tokenizer.json')          1MB
    Download "$camBase/sentencepiece.bpe.model" (Join-Path $camDir 'sentencepiece.bpe.model') 500KB
    Download "$camBase/config.json"             (Join-Path $camDir 'config.json')             100
    Download "$camBase/tokenizer_config.json"   (Join-Path $camDir 'tokenizer_config.json')   100
    Download "$camBase/special_tokens_map.json" (Join-Path $camDir 'special_tokens_map.json') 100
    Write-WorkflowMessage -Output $WorkflowOutput -Message "camembert-base staged under $camDir"
} else {
    Write-WorkflowMessage -Output $WorkflowOutput -Message 'skipped camembert-base (pass -WithCamembert to fetch ~440 MB reranker model)'
}

$nativeCount = (Get-ChildItem $TargetNative -File -ErrorAction SilentlyContinue | Measure-Object).Count
$modelCount  = (Get-ChildItem $TargetModels -File -ErrorAction SilentlyContinue | Measure-Object).Count
$nativeCatalogCount = @($CatalogDlls | Where-Object { Test-Path (Join-Path $TargetNative $_) }).Count
Write-WorkflowStep -Output $WorkflowOutput -Message 'done'
Write-Host "         $TargetNative : $nativeCount file(s)"
Write-Host "         $TargetModels : $modelCount file(s)"
Write-OutputLine -Segments @()
Write-OutputLine -Segments @(
    New-OutputSegment -Text 'Next: ' -Role Category
    New-OutputSegment -Text 'scripts\menu.ps1 (Build & run)' -Role Body
)

$summaryResult = if ($nativeCatalogCount -lt $CatalogDlls.Count) { 'Partial' } else { 'Success' }
$summarySentence = if ($summaryResult -eq 'Partial') {
    "Runtime asset setup finished, but the native runtime catalog is incomplete."
} else {
    "Runtime assets were set up under $TargetRoot."
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result $summaryResult `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        'Data root'      = $TargetRoot
        Native          = $NativeRuntimeStatus
        'Native catalog' = "$nativeCatalogCount / $($CatalogDlls.Count)"
        'Native files'  = $nativeCount
        Models          = $ModelStatus
        'Model files'   = $modelCount
        Force           = $(if ($Force) { 'Yes' } else { 'No' })
    }) `
    -Next @("Run scripts\menu.ps1 and choose Build & run.")
