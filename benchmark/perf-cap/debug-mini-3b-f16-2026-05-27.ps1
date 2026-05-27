# Test Voxtral Mini 3B en FP16 (converti localement depuis safetensors) sur
# les 3 mêmes samples que debug-samples-difficiles-2026-05-27.ps1 — pour
# comparer directement avec Voxtral 24B Q4_K_M sur les patterns de
# dégradation identifiés (je/tu, 0.3.1, termes techniques EN).
#
# Hypothèse Cohere : un modèle plus petit en quant élevé conserve mieux la
# fidélité linguistique fine qu'un modèle plus gros en Q4 agressif.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Llama   = 'D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe'
$Models  = 'D:\models\llm\voxtral'
$Model   = Join-Path $Models 'Voxtral-Mini-3B-2507-F16.gguf'
$Mmproj  = Join-Path $Models 'mmproj-Voxtral-Mini-3B-2507-F16.gguf'

$CorpusDir = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30'
$OutDir    = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\runs\voxtral-debug-0002-mini3b-f16'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$Prompt = 'Transcris cet audio en français.'

$samples = @(
    @{ Label='S0_court_dcad692a';        File='dcad692a54fd452cbfb174ca9899deba.wav'; Sec=1.7  }
    @{ Label='S1_moyen_e6db36e7';        File='e6db36e764764be78f7514f5852fac32.wav'; Sec=54.2 }
    @{ Label='S2_long_677a1dee';         File='677a1dee82164efbb3536ef1581b8c9e.wav'; Sec=113.9 }
)

Write-Host ""
Write-Host "=== Voxtral Mini 3B FP16 sur 3 samples (mêmes que debug-0001) ==="
Write-Host "  model  : $Model"
Write-Host "  prompt : $Prompt"
Write-Host ""

# Vérification que le modèle est là — sinon la conversion n'est pas finie
if (-not (Test-Path $Model)) {
    Write-Host "FATAL : modèle introuvable. La conversion safetensors → FP16 n'est"
    Write-Host "        peut-être pas finie. Attendre, puis relancer."
    Write-Host "        Path attendu : $Model"
    exit 1
}
if (-not (Test-Path $Mmproj)) {
    Write-Host "FATAL : mmproj FP16 introuvable. À convertir aussi via :"
    Write-Host "  python convert_hf_to_gguf.py --outfile $Mmproj --outtype f16 --mmproj <safetensors-dir>"
    Write-Host "        Path attendu : $Mmproj"
    exit 1
}

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
Write-Host ""
Write-Host "Comparer avec debug-0001 sur les mêmes 3 samples :"
Write-Host "  - S1 : Voxtral 24B Q4 dit 'si tu t'autorises', perd 0.3.1, oublie 'version Z'"
Write-Host "  - S2 : Voxtral 24B Q4 perd 'loadwindow' (→ 'low wind'), 'clear' (→ 'effacer')"
Write-Host "Vérifier si Mini 3B FP16 préserve ces nuances ou pas."
