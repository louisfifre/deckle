# Test rapide finding 2026-05-27 : injection du token [TRANSCRIBE] dans le
# prompt utilisateur change-t-elle le comportement de Voxtral via mtmd-cli ?
#
# Hypothèse : llama-mtmd-cli pousse Voxtral en mode chat conversationnel par
# défaut (chat template Devstral hérité). Le token spécial [TRANSCRIBE] du
# papier Voxtral devrait faire basculer en mode transcription verbatim.
#
# Test : 1 sample court avec contenu propre ("Et toujours douter un peu",
# 1.7s, dcad692a), 4 prompts différents, comparaison visuelle de la sortie.
#
# Coût : ~1 minute total (4 inférences × 15s chacune sur 24B Q4_K_M).
# Pas de Gemini judge, pas de Whisper, juste Voxtral.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

# Paths
$Llama   = 'D:\workspace\llama.cpp\build\bin\llama-mtmd-cli.exe'
$Models  = 'D:\models\llm\voxtral'
$Model   = Join-Path $Models 'Voxtral-Small-24B-2507-Q4_K_M.gguf'
$Mmproj  = Join-Path $Models 'mmproj-Voxtral-Small-24B-2507.gguf'
$Audio   = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\corpora\voxtral-val-30\dcad692a54fd452cbfb174ca9899deba.wav'

$OutDir  = 'C:\Users\Louis\AppData\Local\Deckle\benchmark\runs\voxtral-debug-0001'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$prompts = @(
    @{ Label='P0_baseline_current';      Prompt='Transcris cet audio en français.' }
    @{ Label='P1_transcribe_token_lang'; Prompt='lang:fr [TRANSCRIBE]' }
    @{ Label='P2_transcribe_token_only'; Prompt='[TRANSCRIBE]' }
    @{ Label='P3_lang_only';             Prompt='lang:fr' }
)

Write-Host ""
Write-Host "=== Test [TRANSCRIBE] token — Voxtral 24B Q4_K_M via mtmd-cli ==="
Write-Host "  audio  : dcad692a (1.7s, 'Et toujours douter un peu')"
Write-Host "  model  : Voxtral-Small-24B-2507-Q4_K_M"
Write-Host "  ground : 'et toujours douter un peu.'  (Gemini)"
Write-Host "  out    : $OutDir"
Write-Host ""

foreach ($p in $prompts) {
    $log = Join-Path $OutDir "$($p.Label).log"
    Write-Host "─── $($p.Label) ────────────────"
    Write-Host "  prompt : `"$($p.Prompt)`""

    $t0 = Get-Date
    & $Llama `
        --model         $Model `
        --mmproj        $Mmproj `
        --audio         $Audio `
        --n-gpu-layers  99 `
        --ctx-size      4096 `
        --n-predict     128 `
        --temp          0.0 `
        --prompt        $p.Prompt `
        2>&1 | Tee-Object -FilePath $log | Out-Null
    $dt = (Get-Date) - $t0

    # Extraire la transcription : stdout pur, ignorer les lignes de log
    # llama.cpp (qui partent normalement sur stderr mais 2>&1 mélange).
    $text = (Get-Content $log -Raw -ErrorAction SilentlyContinue) -replace '\x1B\[[0-?]*[ -/]*[@-~]', ''
    # Sortie modèle : prendre les lignes après le dernier prompt et avant
    # les stats de fin. Heuristique simple : extraire la portion entre
    # `prompt processing done` (si présent) et `eval time` (stats finales).
    # Affiche tout le log brut côté human pour inspection visuelle.

    Write-Host "  done in $('{0:N1}' -f $dt.TotalSeconds) s, exit=$LASTEXITCODE"
    Write-Host "  log    : $log"
    Write-Host ""
}

Write-Host "─── tous les tests terminés ───"
Write-Host ""
Write-Host "À LIRE : ouvrir les 4 logs dans VSCodium, identifier la transcription"
Write-Host "réelle dans chaque (les logs llama.cpp sont verbeux, la transcription"
Write-Host "est typiquement après le bloc 'audio prefill'). Comparer aux notes"
Write-Host "Gemini (vérité) et aux 4 prompts."
Write-Host ""
Write-Host "Hypothèse à vérifier :"
Write-Host "  - P1 (lang:fr [TRANSCRIBE]) sort une transcription verbatim propre"
Write-Host "    → token reconnu, on a trouvé la voie."
Write-Host "  - P1 contient '[TRANSCRIBE]' littéralement dans la sortie ou"
Write-Host "    dégénère → token PAS reconnu, autre voie nécessaire."
