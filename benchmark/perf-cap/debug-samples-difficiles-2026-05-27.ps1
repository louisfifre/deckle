# Reproduit la dégradation Voxtral 24B Q4_K_M sur des samples connus pour
# poser problème (notes Louis du 2026-05-27).
#
# Trois samples ciblés pour leur profil :
#  - dcad692a (1.7s, propre)        — contrôle, le 24B Q4 le passe bien
#  - e6db36e7 (54.2s, problématique) — Louis a noté : T1 dit "tu" au lieu de "je",
#                                       a oublié une note de version (.1)
#  - 677a1dee (113.9s, long)         — Louis a noté : "Voxral a complètement
#                                       oublié plein de choses"
#
# Un seul prompt par sample : notre baseline actuel (qui marche sur le
# sample court). Objectif : reproduire la dégradation, pas tester des
# variantes de prompt — on sait maintenant que le prompt baseline est OK.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Llama   = 'D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe'
$Models  = 'D:\models\llm\voxtral'
$Model   = Join-Path $Models 'Voxtral-Small-24B-2507-Q4_K_M.gguf'
$Mmproj  = Join-Path $Models 'mmproj-Voxtral-Small-24B-2507.gguf'

$CorpusDir = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30'
$OutDir    = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\runs\voxtral-debug-0001'

$Prompt = 'Transcris cet audio en français.'

$samples = @(
    @{ Label='S0_court_propre_dcad692a';        File='dcad692a54fd452cbfb174ca9899deba.wav'; Sec=1.7;   Gemini='et toujours douter un peu.' }
    @{ Label='S1_moyen_problematique_e6db36e7'; File='e6db36e764764be78f7514f5852fac32.wav'; Sec=54.2;  Gemini='(voir corpus.jsonl)' }
    @{ Label='S2_long_677a1dee';                File='677a1dee82164efbb3536ef1581b8c9e.wav'; Sec=113.9; Gemini='(voir corpus.jsonl)' }
)

Write-Host ""
Write-Host "=== Voxtral 24B Q4_K_M sur 3 samples ciblés ==="
Write-Host "  prompt : $Prompt"
Write-Host ""

foreach ($s in $samples) {
    $audio = Join-Path $CorpusDir $s.File
    $log   = Join-Path $OutDir "$($s.Label).log"
    if (-not (Test-Path $audio)) {
        Write-Host "── SKIP $($s.Label) : audio introuvable ($audio)"
        continue
    }
    Write-Host "─── $($s.Label) ($($s.Sec)s) ────"
    Write-Host "  Gemini ref : $($s.Gemini)"

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
