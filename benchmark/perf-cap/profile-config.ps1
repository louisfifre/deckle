# Profile une config GGUF en mode transcription via llama-mtmd-cli avec
# capture des Vulkan timings (GGML_VK_PERF_LOGGER=1). Sortie : un log
# texte brut dans runs/perf-cap/<config>.log, parsable ensuite par
# parse-vulkan-log.py.
#
# Une invocation = une manche de caractérisation. Run-all.ps1 boucle
# sur la liste des 9 configs.
#
# Resume-safe : si le log existe et est non-vide, skip (force avec
# -Force pour ré-exécuter).

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

    # Mettre mmproj sur CPU pour épargner la VRAM. Utile pour les 24B
    # K_L et Q6_K (modèle 17-19 GB sur 19.65 GB de VRAM) — le mmproj
    # tourne en prefill une seule fois, l'overhead CPU y est acceptable.
    [switch]$MmprojOnCpu,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Validation des chemins. On veut planter tôt si quelque chose manque
# plutôt que partir sur une commande qui produira un log de 5 octets.
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

# GGML_VK_PERF_LOGGER=1 active la capture des Vulkan timings par op
# dans la sortie stderr. C'est la matière première du parser.
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

# 2>&1 fusionne stderr (où vivent les Vulkan timings et la plupart des
# traces llama.cpp) dans stdout, qu'on capture intégralement dans le log.
# Tee-Object permet de voir la progression en console tout en écrivant.
& $LlamaMtmdCli @args 2>&1 | Tee-Object -FilePath $logPath

$exit = $LASTEXITCODE
$dt = (Get-Date) - $t0

Write-Host ""
Write-Host ("[profile] $ConfigName terminé en {0:N1} s (exit code $exit)" -f $dt.TotalSeconds)

if ($exit -ne 0) {
    Write-Warning "llama-mtmd-cli exit code $exit — log potentiellement incomplet."
    exit $exit
}

# Sanity check : un log de caractérisation gen-time doit contenir
# plusieurs blocs "Vulkan Timings:" et le marqueur "Total time:". Si
# absent, la capture des timings a échoué (GGML_VK_PERF_LOGGER ignoré ?).
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
