# Session 2026-05-26 : caractériser le régime stable du Q4_K_M (45 vs 7.5 tok/s).
# 5 reruns consécutifs avec gpu_monitor.ps1 en background par run.
# Logs étiquetés rerun3..rerun7 pour ne pas écraser rerun1/rerun2 existants.

[CmdletBinding()]
param(
    [int[]]$RunIds = @(3, 4, 5, 6, 7),
    [int]$SamplerIntervalMs = 250
)

$ErrorActionPreference = 'Stop'

$PerfCapDir   = $PSScriptRoot
$BenchDir     = Split-Path $PerfCapDir -Parent
$ModelsDir    = Join-Path $BenchDir 'models-cache'
$OutputDir    = Join-Path $BenchDir 'runs\perf-cap'
$MonitorScript = Join-Path $BenchDir 'lib\monitor\gpu_monitor.ps1'
$ProfileScript = Join-Path $PerfCapDir 'profile-config.ps1'
$ParseScript   = Join-Path $PerfCapDir 'parse_vulkan_log.py'

$ModelPath  = Join-Path $ModelsDir 'Voxtral-Small-24B-2507-Q4_K_M.gguf'
$MmprojPath = Join-Path $ModelsDir 'mmproj-Voxtral-Small-24B-2507.gguf'
$AudioPath  = Join-Path $ModelsDir 'sample-bc08abb2.wav'

$MonitorDir = Join-Path $OutputDir 'monitor'
if (-not (Test-Path $MonitorDir)) {
    New-Item -ItemType Directory -Path $MonitorDir -Force | Out-Null
}

$jsonlPath = Join-Path $OutputDir 'aggregated.jsonl'
$modelSize = (Get-Item $ModelPath).Length

foreach ($id in $RunIds) {
    $slug = "voxtral-24b-q4_k_m-rerun$id"
    $monitorOut = Join-Path $MonitorDir "$slug-monitor.jsonl"

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════"
    Write-Host "  $slug — start $(Get-Date -Format 'HH:mm:ss')"
    Write-Host "═══════════════════════════════════════════════════════════════"

    # gpu_monitor en background : Start-Job pour pouvoir stop proprement.
    # Cadence 250ms d'extra-sleep entre samples = ~1.25 s/sample (Get-Counter
    # impose 1s minimum). Sur ~45s de run, ça donne ~36 samples — assez
    # pour voir si le régime est stable ou oscille.
    $job = Start-Job -ScriptBlock {
        param($script, $out, $intervalMs)
        & $script -OutFile $out -IntervalMs $intervalMs
    } -ArgumentList $MonitorScript, $monitorOut, $SamplerIntervalMs

    Write-Host "  monitor job id=$($job.Id) → $monitorOut"

    Start-Sleep -Seconds 2  # let monitor settle one cycle before workload

    $runStart = Get-Date

    & $ProfileScript `
        -ConfigName $slug `
        -Model $ModelPath `
        -Mmproj $MmprojPath `
        -AudioPath $AudioPath `
        -OutputDir $OutputDir `
        -NGpuLayers 99 `
        -CtxSize 8192 `
        -Force

    $exit = $LASTEXITCODE
    $runDur = (Get-Date) - $runStart

    Stop-Job $job -ErrorAction SilentlyContinue | Out-Null
    Remove-Job $job -Force -ErrorAction SilentlyContinue | Out-Null

    Write-Host ("  profile exit=$exit dur={0:N1}s" -f $runDur.TotalSeconds)

    if ($exit -ne 0) {
        Write-Warning "  $slug : profile-config exit code $exit — skip parse"
        continue
    }

    # parse_vulkan_log → append row au JSONL
    $logPath = Join-Path $OutputDir "$slug.log"
    & python $ParseScript `
        --log $logPath `
        --config $slug `
        --audio-duration 12.3 `
        --model-file 'Voxtral-Small-24B-2507-Q4_K_M.gguf' `
        --model-size $modelSize `
        --output $jsonlPath

    Write-Host "  parse exit=$LASTEXITCODE"
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  Done $(Get-Date -Format 'HH:mm:ss'). Monitor logs in $MonitorDir"
Write-Host "═══════════════════════════════════════════════════════════════"
