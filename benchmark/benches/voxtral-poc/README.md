# bench voxtral-poc

Bench du POC d'évaluation Voxtral Mini 3B comme alternative à Whisper
dans le pipeline de transcription Deckle.

## Quoi

Pour chaque sample du corpus `corpora/voxtral-poc/` et chaque régime de
prompt (`V1_raw` à `V5_traduit_en`), le bench :

1. Transcrit via la source choisie (par défaut Voxtral DML).
2. Calcule WER + CER vs la référence Whisper du corpus.
3. Calcule des métriques objectives (looping, hallucinations).
4. Demande à Claude Haiku de noter sur 4 axes (fidélité, propreté,
   hallucinations, respect du régime) + flag `whisper_ref_suspecte`.
5. Écrit une ligne JSON par expérience dans `runs/<run-id>/results.jsonl`.

## Pourquoi

- Phase 1/2 (llama.cpp + chat template Jinja) montraient des résultats
  catastrophiques parce que la stack poussait Voxtral en mode chat
  conversationnel — il paraphrasait au lieu de transcrire.
- Phase 3 (Transformers + `apply_transcription_request`) a corrigé ça
  sur un smoke. Ce bench généralise sur tout le corpus × 5 régimes pour
  quantifier vraiment.

## Comment

```pwsh
# 1. Avoir un .env à la racine du benchmark/ avec ANTHROPIC_API_KEY
#    (voir benchmark/.env.example).

# 2. Avoir un corpus voxtral-poc sous corpora/voxtral-poc/ avec :
#    - corpus.jsonl (lignes payload Deckle telemetry)
#    - *.wav (référencés dans corpus.jsonl payload.audio_file)
#    Les corpora ne sont pas versionnés — chaque utilisateur amène les siens.

# 3. Avoir le venv .venv-voxtral-dml prêt (cf. benchmark/CLAUDE.md).

# 4. Lancer :
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py

# Variantes :
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --limit 3
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --regimes V1_raw
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --skip-judge
.venv-voxtral-dml\Scripts\python.exe benches\voxtral-poc\bench.py --source whisper-cpp
```

## Sortie

`runs/voxtral-poc-<timestamp>/results.jsonl` — une ligne par expérience,
schéma stable :

```json
{
  "audio_id":         "bc08abb2...",
  "audio_seconds":    12.88,
  "tier":             "very-short",
  "reference_text":   "Ça m'a l'air bon...",
  "source":           "voxtral-dml",
  "regime":           "V1_raw",
  "regime_prompt":    "Transcris cet audio en français.",
  "ok":               true,
  "text":             "Ça m'a l'air bon. Je vais voir...",
  "elapsed_s":        2.72,
  "rtf":              0.21,
  "generated_tokens": 38,
  "extras":           { "prep_s": 0.01, "gen_s": 2.7, "tok_per_s": 14, ... },
  "metrics":          { "wer": 0.0, "cer": 0.0, "looping_score": 0.0, ... },
  "judge":            { "axes": {...}, "verdict": "...", "parse_ok": true, ... }
}
```

## Limitations actuelles

- Sources : `voxtral-dml` validé, `whisper-cpp` câblé mais non testé en
  v2 (à valider sur un sample).
- Juge : per-row Haiku seulement. Le mode macro Opus (curation Haiku
  → résumé Opus) est dans `lib/judges/claude.py` mais pas branché ici.
- Pas de fallback automatique fp16 → fp32. Si DML râle, relancer avec
  `--dtype float32`.
