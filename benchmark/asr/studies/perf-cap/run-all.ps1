# Run profile-config then parse_vulkan_log for every config whose GGUF and
# mmproj are present in models-cache; a missing file skips its config.
#
# Output: one raw log per config in runs/perf-cap/<slug>.log, and one JSONL
# row per config in runs/perf-cap/aggregated.jsonl.

[CmdletBinding()]
param(
    [string]$ModelsDir  = "$PSScriptRoot\..\models-cache",
    [string]$OutputDir  = "$PSScriptRoot\..\runs\perf-cap",
    [string]$AudioPath  = "$PSScriptRoot\..\models-cache\sample-bc08abb2.wav",
    [double]$AudioDur   = 12.3,
    [string]$Python     = 'python',
    [string[]]$Only     = @(),
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# One mmproj serves every quantization of a model. Layers = 99 puts every
# layer on the GPU. Past 16 GB of model on a 20 GB card, MmprojOnCpu moves
# the 24B mmproj off the GPU and Ctx shrinks, to leave room for the KV cache.
$configs = @(
    @{ Slug='voxtral-3b-q4_k_m';  Model='Voxtral-Mini-3B-2507-Q4_K_M.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q8_0';    Model='Voxtral-Mini-3B-2507-Q8_0.gguf';    Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-bf16';    Model='Voxtral-Mini-3B-2507-bf16.gguf';    Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q6_k';    Model='Voxtral-Mini-3B-2507-Q6_K.gguf';    Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q3_k_m';  Model='Voxtral-Mini-3B-2507-Q3_K_M.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q2_k';    Model='Voxtral-Mini-3B-2507-Q2_K.gguf';    Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q3_k_m'; Model='Voxtral-Small-24B-2507-Q3_K_M.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q6_k';   Model='Voxtral-Small-24B-2507-Q6_K.gguf';   Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=2048; MmprojOnCpu=$true }

    @{ Slug='voxtral-3b-q3_k_l';  Model='Voxtral-Mini-3B-2507-Q3_K_L.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q4_k_l';  Model='Voxtral-Mini-3B-2507-Q4_K_L.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q5_k_m';  Model='Voxtral-Mini-3B-2507-Q5_K_M.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-3b-q5_k_l';  Model='Voxtral-Mini-3B-2507-Q5_K_L.gguf';  Mmproj='mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf';  Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q3_k_l'; Model='Voxtral-Small-24B-2507-Q3_K_L.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q4_k_m'; Model='Voxtral-Small-24B-2507-Q4_K_M.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q4_k_l'; Model='Voxtral-Small-24B-2507-Q4_K_L.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=8192 }
    @{ Slug='voxtral-24b-q5_k_m'; Model='Voxtral-Small-24B-2507-Q5_K_M.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=4096; MmprojOnCpu=$true }
    @{ Slug='voxtral-24b-q5_k_l'; Model='Voxtral-Small-24B-2507-Q5_K_L.gguf'; Mmproj='mmproj-Voxtral-Small-24B-2507.gguf';    Layers=99; Ctx=2048; MmprojOnCpu=$true }
)

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$jsonlPath = Join-Path $OutputDir 'aggregated.jsonl'
$profileScript = Join-Path $PSScriptRoot 'profile-config.ps1'
$parseScript   = Join-Path $PSScriptRoot 'parse_vulkan_log.py'

$ranked = 0
$skipped = 0
$failed = @()

foreach ($c in $configs) {
    if ($Only.Count -gt 0 -and ($Only -notcontains $c.Slug)) {
        continue
    }

    $modelPath = Join-Path $ModelsDir $c.Model
    if (-not (Test-Path $modelPath)) {
        Write-Host "[wait] $($c.Slug) : modèle absent ($($c.Model)) — skip pour cette passe"
        $skipped++
        continue
    }

    $mmprojPath = Join-Path $ModelsDir $c.Mmproj
    if (-not (Test-Path $mmprojPath)) {
        Write-Host "[wait] $($c.Slug) : mmproj absent ($($c.Mmproj)) — skip"
        $skipped++
        continue
    }

    Write-Host ""
    Write-Host "═══ $($c.Slug) ═══"

    if ($DryRun) {
        Write-Host "  [dry-run] profile-config -ConfigName $($c.Slug) -Model $modelPath -Mmproj $mmprojPath -NGpuLayers $($c.Layers) -CtxSize $($c.Ctx)"
        continue
    }

    $profileArgs = @{
        ConfigName  = $c.Slug
        Model       = $modelPath
        Mmproj      = $mmprojPath
        AudioPath   = $AudioPath
        OutputDir   = $OutputDir
        NGpuLayers  = $c.Layers
        CtxSize     = $c.Ctx
        Force       = $Force.IsPresent
    }
    if ($c.ContainsKey('MmprojOnCpu') -and $c.MmprojOnCpu) {
        $profileArgs.MmprojOnCpu = $true
    }

    & $profileScript @profileArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$($c.Slug) : profile-config exit code $LASTEXITCODE"
        $failed += $c.Slug
        continue
    }

    $logPath = Join-Path $OutputDir "$($c.Slug).log"
    $modelSize = (Get-Item $modelPath).Length

    & $Python $parseScript `
        --log $logPath `
        --config $c.Slug `
        --audio-duration $AudioDur `
        --model-file $c.Model `
        --model-size $modelSize `
        --output $jsonlPath

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "$($c.Slug) : parse_vulkan_log exit code $LASTEXITCODE"
        $failed += $c.Slug
        continue
    }

    $ranked++
}

Write-Host ""
Write-Host "═══ Résultat ═══"
Write-Host "  Rankés : $ranked"
Write-Host "  Skippés : $skipped (modèle ou mmproj absent / config LM-only)"
Write-Host "  Échoués : $($failed.Count)"
$failed | ForEach-Object { Write-Host "    - $_" }
Write-Host "  Output : $jsonlPath"
