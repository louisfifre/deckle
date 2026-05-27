# Test Voxtral Mini 3B Q8_0 (déjà téléchargé de ggml-org) sur les 3 mêmes
# samples que debug-samples-difficiles-2026-05-27.ps1.
#
# Pourquoi Q8_0 et pas FP16 : la conversion locale safetensors→F16 plante
# sur le tokenizer Tekken (--mistral-format ne sait pas mapper les tensors
# mmproj quand on convertit le LM seul). Q8_0 = ~98-99% qualité FP16 selon
# la littérature LLM. Si Q8_0 résout les patterns d'erreur du 24B Q4_K_M,
# on a la réponse sans creuser plus loin.
#
# Comparaison cible avec voxtral-debug-0001 (24B Q4_K_M) :
#  - S1 (54.2s) : 24B Q4 disait "tu" au lieu de "je", perdait "0.3.1"
#  - S2 (113.9s) : 24B Q4 ratait "loadwindow" (→ "low wind"), "clear" (→ "effacer")

[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Llama   = 'D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe'
$Models  = 'D:\models\llm\voxtral'
$Model   = Join-Path $Models 'Voxtral-Mini-3B-2507-Q8_0.gguf'
$Mmproj  = Join-Path $Models 'mmproj-Voxtral-Mini-3B-2507-Q8_0.gguf'

$CorpusDir = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30'
$OutDir    = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\runs\voxtral-debug-0002-mini3b-q8'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$Prompt = 'Transcris cet audio en français.'

$samples = @(
    @{ Label='S0_court_dcad692a';        File='dcad692a54fd452cbfb174ca9899deba.wav'; Sec=1.7  }
    @{ Label='S1_moyen_e6db36e7';        File='e6db36e764764be78f7514f5852fac32.wav'; Sec=54.2 }
    @{ Label='S2_long_677a1dee';         File='677a1dee82164efbb3536ef1581b8c9e.wav'; Sec=113.9 }
)

Write-Host ""
Write-Host "=== Voxtral Mini 3B Q8_0 sur 3 samples (mêmes que debug-0001) ==="
Write-Host "  model  : $Model"
Write-Host "  mmproj : $Mmproj"
Write-Host "  prompt : $Prompt"
Write-Host ""

foreach ($s in $samples) {
    $audio = Join-Path $CorpusDir $s.File
    $log   = Join-Path $OutDir "$($s.Label).log"
    if (-not (Test-Path $audio)) {
        Write-Host "── SKIP $($s.Label) : audio introuvable"
        continue
    }
    Write-Host "─── $($s.Label) ($($s.Sec)s) ────"

    $t0 = Get-Date
    $maxTok = [Math]::Max(128, [Math]::Ceiling($s.Sec * 4))
    & $Llama `
        --model         $Model `
        --mmproj        $Mmproj `
        --audio         $audio `
        --n-gpu-layers  99 `
        --ctx-size      4096 `
        --n-predict     $maxTok `
        --temp          0.0 `
        --prompt        $Prompt `
        2>&1 | Tee-Object -FilePath $log | Out-Null
    $dt = (Get-Date) - $t0

    Write-Host "  done in $('{0:N1}' -f $dt.TotalSeconds) s, exit=$LASTEXITCODE"
    Write-Host "  log    : $log"
    Write-Host ""
}

Write-Host "─── all done ───"
