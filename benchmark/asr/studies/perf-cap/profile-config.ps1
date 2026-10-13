# Profile one GGUF config transcribing through llama-mtmd-cli, with Vulkan
# timings captured (GGML_VK_PERF_LOGGER=1). Output: a raw log in
# runs/perf-cap/<config>.log for parse_vulkan_log.py. run-all.ps1 calls it
# once per config.
#
# A non-empty log already there skips the run; -Force runs it again.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ConfigName,

    [Parameter(Mandatory)]
    [string]$Model,

    [Parameter(Mandatory)]
    [string]$Mmproj,

    [string]$AudioPath = "$PSScriptRoot\..\models-cache\sample-bc08abb2.wav",

    [string]$Prompt = "Transcris cet audio en français.",

    [string]$OutputDir = "$PSScriptRoot\..\runs\perf-cap",

    [string]$LlamaMtmdCli = "D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe",

    [int]$NGpuLayers = 99,
    [int]$CtxSize    = 8192,
    [int]$MaxTokens  = 256,

    # Keep the mmproj on the CPU to spare VRAM for the largest 24B models.
    # It runs once, at prefill, so the CPU cost is acceptable.
    [switch]$MmprojOnCpu,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Fail early on a missing path rather than run a command that writes a
# near-empty log.
foreach ($p in @($LlamaMtmdCli, $Model, $Mmproj, $AudioPath)) {
    if (-not (Test-Path $p)) {
        Write-Error "Chemin introuvable : $p"
        exit 1
    }
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$OutputDir = (Resolve-Path $OutputDir).Path

$logPath = Join-Path $OutputDir "$ConfigName.log"

if ((Test-Path $logPath) -and -not $Force) {
    $size = (Get-Item $logPath).Length
    if ($size -gt 0) {
        Write-Host "[skip] $ConfigName : log déjà présent ($size octets). -Force pour ré-exécuter."
        exit 0
    }
}

# GGML_VK_PERF_LOGGER=1 prints per-op Vulkan timings on stderr, the
# parser's input.
$env:GGML_VK_PERF_LOGGER = '1'

$args = @(
    '--model',         $Model,
    '--mmproj',        $Mmproj,
    '--audio',         $AudioPath,
    '--prompt',        $Prompt,
    '--n-gpu-layers',  $NGpuLayers,
    '--ctx-size',      $CtxSize,
    '--n-predict',     $MaxTokens,
    '--temp',          '0.0',
    '--log-prefix',    '--log-timestamps'
)

if ($MmprojOnCpu.IsPresent) {
    $args += '--no-mmproj-offload'
}

Write-Host "[profile] $ConfigName"
Write-Host "  Model  : $(Split-Path $Model -Leaf)"
Write-Host "  Mmproj : $(Split-Path $Mmproj -Leaf)"
Write-Host "  Audio  : $(Split-Path $AudioPath -Leaf)"
Write-Host "  Layers : $NGpuLayers / Ctx : $CtxSize / MaxTok : $MaxTokens"
Write-Host "  Log    : $logPath"
Write-Host ""

$t0 = Get-Date

# The Vulkan timings and most llama.cpp traces go to stderr, hence 2>&1.
& $LlamaMtmdCli @args 2>&1 | Tee-Object -FilePath $logPath

$exit = $LASTEXITCODE
$dt = (Get-Date) - $t0

Write-Host ""
Write-Host ("[profile] $ConfigName terminé en {0:N1} s (exit code $exit)" -f $dt.TotalSeconds)

if ($exit -ne 0) {
    Write-Warning "llama-mtmd-cli exit code $exit — log potentiellement incomplet."
    exit $exit
}

# A generation log holds many "Vulkan Timings:" blocks; a handful means
# the perf logger did not run.
$content = Get-Content $logPath -Raw
$blockCount = ([regex]::Matches($content, 'Vulkan Timings:')).Count
$totalCount = ([regex]::Matches($content, 'Total time:')).Count

Write-Host "  Vulkan Timings blocks : $blockCount"
Write-Host "  Total time lines      : $totalCount"

if ($blockCount -lt 5) {
    Write-Warning "Très peu de blocs Vulkan Timings ($blockCount). GGML_VK_PERF_LOGGER actif ?"
    exit 2
}

exit 0
