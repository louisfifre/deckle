---
name: bench-voxtral-validation
description: "Bench de validation Voxtral 24B Q4_K_M comme remplacement de Whisper, ground truth Gemini, 30 samples stratifiés par durée."
type: bench-scenario
---

# `studies/voxtral-validation/` — Voxtral vs Whisper

Bench de qualité Voxtral 24B Q4_K_M (via `llama-mtmd-cli` Vulkan) sur le corpus `voxtral-val-30`. Mesure WER, word_count_ratio et axes paralinguistiques du judge Gemini multimodal.

> **Statut — étude archivée (abandonnée 2026-06).** Whisper streaming couvre le besoin ; l'évaluation Voxtral est abandonnée. Conservé comme référence (ground truth Gemini, source llama.cpp Vulkan, `word_count_ratio`). Voir [`../../README.md`](../../README.md) et [`../../JOURNAL.md`](../../JOURNAL.md).

## Pourquoi ce bench

Le POC Voxtral a tranché en faveur de Voxtral sur l'axe perf (730 GB/s, 47 tok/s, RTF 0.05 sur Q4_K_M). Reste à valider la qualité sur un corpus représentatif. La référence Whisper hallucine régulièrement (chaînes type « Sous-titrage Société Radio-Canada », bouclage sur les longs) — méthodologiquement inutilisable comme ground truth. On bascule sur Gemini comme transcripteur ground truth, parce que Gemini écoute l'audio brut (`Part.from_bytes`) et produit une transcription directement comparable au signal.

## Préalables

1. **Corpus construit.** Lancer `python benchmark/asr/build_corpus.py --corpus voxtral-val-30` pour extraire 30 samples stratifiés depuis `%LOCALAPPDATA%\Deckle\telemetry\`.
2. **Ground truth présente.** Le `corpus.jsonl` doit déjà contenir `payload.reference_text_gemini` pour les samples à juger.
3. **Clé Gemini.** `benchmark/.env` contient `GEMINI_API_KEY=AIza...`.
4. **`google-genai` installé.** `pip install google-genai jiwer soundfile`.
5. **Binaire `llama-mtmd-cli.exe`** à `D:\workspace\llama.cpp\build\bin\` (la session perf-cap a validé Voxtral 24B Q4_K_M via cette stack).

## Usage

```powershell
# Run complet (30 samples × 6 régimes = 180 rows + 180 appels judge Gemini)
python bench.py

# Sous-set régimes
python bench.py --regimes T1_baseline,T2_verbatim

# Smoke 3 samples
python bench.py --limit 3

# Sans judge (métriques objectives seules — plus rapide, pas de quota Gemini consommé)
python bench.py --skip-judge
```

Sorties dans `runs/voxtral-validation-<id>/` : `results.jsonl` (un row par sample × régime), `events.jsonl` (lifecycle bench).

## Métriques produites

Par row :

- **WER** et **CER** contre la référence Gemini verbatim.
- **`word_count_ratio`** = `words(hyp) / words(ref_gemini)`. Proxy lisible pour détecter le lissage (ratio < 0.7) versus la fidélité verbatim (ratio 0.9–1.1).
- **`looping_score`** : score n-gramme répété (>= 2× sur fenêtres glissantes).
- **`hallucination_hits`** et **`custom_leak_hits`** : regex sur les chaînes connues (Société Radio-Canada, Amara.org, etc.).
- **RTF** : Real-Time Factor (elapsed / audio_s).
- **Axes judge Gemini multimodal** (0–100) : `fidelite_signal`, `proprete`, `absence_hallucination`, `regime_respecte` + booléen `whisper_ref_suspecte` + verdict court.

## Décision attendue

Le bench instrumente la donnée ; il **ne tranche pas**. La décision finale (Voxtral remplace Whisper / Voxtral reste optionnel / on garde Whisper) se prend en lisant les JSONL et en évaluant à la main les régimes selon les axes prioritaires fixés par le POC (fidélité, propreté, absence de bouclage/hallucinations).

Si le baseline n'est pas net, la skill `autoresearch` peut prendre le relais pour explorer l'espace des prompts (phase 3 du POC).
