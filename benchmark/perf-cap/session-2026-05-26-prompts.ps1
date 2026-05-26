# Session 2026-05-26 : tester ce que Voxtral 24B Q4_K_M accepte comme
# instructions via llama-mtmd-cli (stack Vulkan). Six régimes — du
# transcribe brut au système-prompt + Q&A — pour cartographier ce que
# le modèle accepte en mode chat.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$BenchDir   = Split-Path $PSScriptRoot -Parent
$ModelsDir  = Join-Path $BenchDir 'models-cache'
$OutputDir  = Join-Path $BenchDir 'runs\perf-cap\prompts-2026-05-26'
$Llama      = 'D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe'
$Model      = Join-Path $ModelsDir 'Voxtral-Small-24B-2507-Q4_K_M.gguf'
$Mmproj     = Join-Path $ModelsDir 'mmproj-Voxtral-Small-24B-2507.gguf'
$Audio      = Join-Path $ModelsDir 'sample-bc08abb2.wav'

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Liste (label, system, prompt, maxtok). $null pour system = pas de -sys.
$regimes = @(
    @{ Label='T1_baseline';    Sys=$null; Prompt='Transcris cet audio en français.'; MaxTok=128 }
    @{ Label='T2_verbatim';    Sys=$null; Prompt='Transcris cet audio en français, mot pour mot, en conservant les hésitations comme « euh » ou « ben » et les répétitions si présentes.'; MaxTok=128 }
    @{ Label='T3_translate';   Sys=$null; Prompt='Translate this French audio to natural English. Output only the translation, no preamble.'; MaxTok=128 }
    @{ Label='T4_summary';     Sys=$null; Prompt='Résume en une phrase courte ce que dit la personne dans cet audio.'; MaxTok=80 }
    @{ Label='T5_qa_register'; Sys=$null; Prompt='Décris le ton et le registre de cette personne en une phrase courte (formel, informel, hésitant, ...). Ne transcris pas.'; MaxTok=80 }
    @{ Label='T6_sys_prompt';  Sys='Tu es un assistant de transcription pour des dictées en français. Tu rends toujours la transcription verbatim suivie, sur une nouvelle ligne, d''une étiquette entre crochets indiquant le ton détecté.'; Prompt='Transcris cet audio.'; MaxTok=128 }
)

foreach ($r in $regimes) {
    $log = Join-Path $OutputDir "$($r.Label).log"
    Write-Host ""
    Write-Host "═══ $($r.Label) ═══"
    if ($r.Sys) { Write-Host "  sys    : $($r.Sys.Substring(0, [Math]::Min(80, $r.Sys.Length)))..." }
    Write-Host "  prompt : $($r.Prompt.Substring(0, [Math]::Min(80, $r.Prompt.Length)))..."

    $args = @(
        '--model', $Model,
        '--mmproj', $Mmproj,
        '--audio', $Audio,
        '--n-gpu-layers', '99',
        '--ctx-size', '4096',
        '--n-predict', $r.MaxTok,
        '--temp', '0.0',
        '--prompt', $r.Prompt
    )
    if ($r.Sys) {
        $args += '--system-prompt'
        $args += $r.Sys
    }

    $t0 = Get-Date
    & $Llama @args 2>&1 | Tee-Object -FilePath $log | Out-Null
    $dt = (Get-Date) - $t0
    Write-Host ("  done in {0:N1} s, exit=$LASTEXITCODE" -f $dt.TotalSeconds)
}

Write-Host ""
Write-Host "═══ All regimes finished — logs in $OutputDir ═══"
