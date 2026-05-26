# Profile une config GGUF LM-only via llama-server, pour les modèles
# qui n'ont pas de mmproj audio (Mistral Small 24B base, contrôle archi
# de Voxtral Small 24B).
#
# Différent de profile-config.ps1 :
#   - llama-server au lieu de llama-mtmd-cli (pas de --mmproj).
#   - Capture les Vulkan timings via GGML_VK_PERF_LOGGER sur stderr du
#     server (même format que mtmd-cli, le parser existant marche).
#   - Pas d'audio prefill, juste un text prompt qui génère ~30 tokens.
#
# Le serveur tourne en background pendant la requête, puis est tué.
# Le log accumulé contient warmup + prefill + gen blocks comme avec
# mtmd-cli, classifiables par le parser.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ConfigName,

    [Parameter(Mandatory)]
    [string]$Model,

    [string]$Prompt = "Explain in one paragraph what a transformer neural network is.",

    [string]$OutputDir = "$PSScriptRoot\..\runs\perf-cap",

    [string]$LlamaServer = "D:\workspace\llama.cpp\build\bin\llama-server.exe",

    [int]$NGpuLayers = 99,
    [int]$CtxSize    = 4096,
    [int]$MaxTokens  = 64,
    [int]$Port       = 28080,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

foreach ($p in @($LlamaServer, $Model)) {
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
        Write-Host "[skip] $ConfigName : log présent ($size octets). -Force pour ré-exécuter."
        exit 0
    }
}

Write-Host "[profile-server] $ConfigName"
Write-Host "  Model  : $(Split-Path $Model -Leaf)"
Write-Host "  Layers : $NGpuLayers / Ctx : $CtxSize / MaxTok : $MaxTokens / Port : $Port"
Write-Host "  Log    : $logPath"
Write-Host ""

# Lance llama-server en background. GGML_VK_PERF_LOGGER=1 force la
# capture des Vulkan timings sur stderr (où Tee-Object envoie aussi).
$env:GGML_VK_PERF_LOGGER = '1'

# On capture stderr+stdout dans le log via 2>&1 et redirection PowerShell.
# Le processus tourne sans interaction utilisateur — pas de --host
# externe, juste loopback.
$serverArgs = @(
    '--model',         $Model,
    '--n-gpu-layers',  $NGpuLayers,
    '--ctx-size',      $CtxSize,
    '--host',          '127.0.0.1',
    '--port',          $Port,
    '--no-webui',
    '--log-disable'    # désactive le formatage HTTP des logs pour garder Vulkan timings propres
)

$serverProc = Start-Process -FilePath $LlamaServer -ArgumentList $serverArgs `
    -RedirectStandardError $logPath `
    -RedirectStandardOutput "$logPath.stdout" `
    -PassThru -WindowStyle Hidden

Write-Host "  Server PID : $($serverProc.Id)"

# Poll /health jusqu'à ce que le serveur soit prêt (max 90 s pour
# accommoder le chargement d'un 16 GB GGUF depuis NVMe).
$ready = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2 -ErrorAction Stop
        if ($health.status -eq 'ok') { $ready = $true; break }
    } catch {
        # serveur pas encore prêt
    }
    Start-Sleep -Milliseconds 1000
}

if (-not $ready) {
    Write-Warning "Serveur pas prêt après 90 s — abandon"
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    exit 2
}

Write-Host "  Serveur prêt, envoi requête text completion..."

# Requête /v1/completions (raw, pas chat) pour éviter le chat template
# qui ajoute du wrapping autour du prompt. On veut juste mesurer le
# throughput de gen sur n tokens.
$body = @{
    prompt      = $Prompt
    n_predict   = $MaxTokens
    temperature = 0.0
    stream      = $false
} | ConvertTo-Json -Depth 10

$t0 = Get-Date
try {
    $resp = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/completion" `
        -Method POST -ContentType 'application/json' `
        -Body $body -TimeoutSec 300
} catch {
    Write-Warning "Échec requête : $_"
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    exit 3
}
$dt = (Get-Date) - $t0

Write-Host "  Latence : $([math]::Round($dt.TotalSeconds,2)) s"
if ($resp.tokens_predicted) {
    Write-Host "  Tokens gen (rapporté par server) : $($resp.tokens_predicted)"
}

# Stop le serveur proprement (Stop-Process flushe les buffers en sortie).
Stop-Process -Id $serverProc.Id -Force
Start-Sleep -Milliseconds 500

# Sanity check : le log doit contenir des blocs Vulkan Timings.
$content = Get-Content $logPath -Raw
$blockCount = ([regex]::Matches($content, 'Vulkan Timings:')).Count
$totalCount = ([regex]::Matches($content, 'Total time:')).Count

Write-Host "  Vulkan Timings blocks : $blockCount"
Write-Host "  Total time lines      : $totalCount"

if ($blockCount -lt 5) {
    Write-Warning "Très peu de blocs Vulkan Timings ($blockCount). GGML_VK_PERF_LOGGER actif ?"
    exit 4
}

exit 0
