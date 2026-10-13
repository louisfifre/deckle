# Profile a text-only GGUF through llama-server, for a model with no audio
# mmproj (Mistral Small 24B, the architecture control for Voxtral Small 24B).
#
# The server's stderr carries the same Vulkan timings format as
# llama-mtmd-cli, so parse_vulkan_log.py reads this log too. No audio
# prefill: one text prompt. The server runs for the one request, then is
# killed.

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

# GGML_VK_PERF_LOGGER=1 prints per-op Vulkan timings on the server's
# stderr, which Start-Process redirects into the log.
$env:GGML_VK_PERF_LOGGER = '1'

$serverArgs = @(
    '--model',         $Model,
    '--n-gpu-layers',  $NGpuLayers,
    '--ctx-size',      $CtxSize,
    '--host',          '127.0.0.1',
    '--port',          $Port,
    '--no-webui',
    '--log-disable'    # keeps the server's own log lines out of the Vulkan timings
)

$serverProc = Start-Process -FilePath $LlamaServer -ArgumentList $serverArgs `
    -RedirectStandardError $logPath `
    -RedirectStandardOutput "$logPath.stdout" `
    -PassThru -WindowStyle Hidden

Write-Host "  Server PID : $($serverProc.Id)"

# 90 s leaves time to load a 16 GB GGUF.
$ready = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 2 -ErrorAction Stop
        if ($health.status -eq 'ok') { $ready = $true; break }
    } catch {
        # server not ready yet
    }
    Start-Sleep -Milliseconds 1000
}

if (-not $ready) {
    Write-Warning "Serveur pas prêt après 90 s — abandon"
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    exit 2
}

Write-Host "  Serveur prêt, envoi requête text completion..."

# The raw /completion endpoint, not chat, so no chat template wraps the
# prompt.
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

# Kill the server, then give the redirected log a moment to finish writing.
Stop-Process -Id $serverProc.Id -Force
Start-Sleep -Milliseconds 500

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
