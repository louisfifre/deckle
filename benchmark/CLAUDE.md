# CLAUDE.md — `benchmark/`

Instructions pour agent travaillant sur le banc de mesure Voxtral / Whisper / futurs.

## Identité du dossier

Le `benchmark/` est une **boîte autonome** dédiée à mesurer la qualité
et les performances de différents backends ASR (Whisper, Voxtral, à
venir) sur des corpora privés. Il sera extrait dans son propre repo
plus tard ; en attendant il vit dans le repo Deckle pour rester proche
des données de télémétrie qui alimentent les corpora.

Trois objectifs guident chaque choix ici :

- **Lisibilité par agent avant lisibilité par humain.** L'utilisateur
  n'ira pas fouiller dans les fichiers — c'est un agent qui orchestre.
  Donc docstrings riches, headers explicites, vocabulaire stable.
- **Réutilisabilité par concept** (sources, juges, corpora, prompts,
  métriques). Ajouter un nouveau backend = ajouter un fichier dans
  `lib/sources/` qui implémente le contrat. Pas de duplication.
- **Privacy** : les corpora contiennent de l'audio utilisateur, **jamais
  versionnés**. Chaque machine amène ses propres samples.

## Organisation

```
benchmark/
├── CLAUDE.md             # ce fichier
├── README.md             # vue humaine, plus court
├── .env.example          # template clé Anthropic (le .env est gitignored)
│
├── lib/                  # briques réutilisables, transverses aux benches
│   ├── corpus.py         #   loader corpus.jsonl → list[Sample]
│   ├── env.py            #   load_dotenv() minimal sans dépendance
│   ├── _base_compat.py   #   utilitaires (force stdout UTF-8 sur Windows)
│   ├── sources/          #   drivers ASR (un fichier = un backend)
│   │   ├── _base.py      #     contrat Source.transcribe() → Transcription
│   │   ├── voxtral_dml.py    #   Voxtral Transformers + torch-directml
│   │   └── whisper_cpp.py    #   Whisper.cpp via whisper-cli.exe
│   ├── judges/           #   évaluateurs LLM
│   │   ├── _base.py      #     contrat Judge.score_row() / score_macro()
│   │   └── claude.py     #     API Anthropic, Haiku per-row + Opus macro
│   ├── metrics/          #   règles objectives, pas d'appel LLM
│   │   ├── wer.py        #     WER + CER via jiwer
│   │   ├── looping.py    #     détection bouclage n-gram
│   │   └── leak.py       #     hallucinations connues + leak custom
│   └── monitor/
│       └── gpu_monitor.ps1   # script PowerShell GPU/RAM (lancement manuel)
│
├── corpora/              # GITIGNORED — chacun ses samples
│   └── voxtral-poc/      #   exemple : corpus.jsonl + *.wav
│
├── prompts/              # versionnés, immuables
│   ├── transcription/    #   prompts à passer aux sources
│   │   └── voxtral_regimes.toml   # V1..V5
│   ├── judges/           #   system prompts pour les juges
│   │   └── claude_per_row.md
│   └── whisper_initial.txt        # initial prompt Whisper Deckle
│
├── benches/              # un sous-dossier = un scénario benché
│   └── voxtral-poc/
│       ├── bench.py      #     orchestrateur
│       └── README.md     #     description du scénario
│
├── runs/                 # GITIGNORED — outputs jetables
└── models-cache/         # GITIGNORED — GGUF, safetensors locaux
```

## Concepts

### Source

Une **source** est un backend de transcription. Elle expose
`transcribe(audio_path, prompt, max_new_tokens) → Transcription`. Le
contrat est minimal pour qu'un bench puisse swap d'une source à l'autre.

Pour ajouter une source :

1. Créer `lib/sources/<name>.py` qui définit une classe héritant
   (implicitement, duck-typing OK) de `lib.sources._base.Source`.
2. La classe instancie le modèle dans `__init__` (chargement coûteux,
   payé une fois). `transcribe()` est appelé en boucle, doit être rapide.
3. Mettre à jour le `--source` dans les benches qui veulent l'utiliser.

### Judge

Un **juge** note des transcriptions. Deux modes :

- `score_row(hypothesis, reference, regime, source) → JudgeScore` :
  per-row, modèle léger (Claude Haiku), appelé dans la boucle.
- `score_macro(run_summary, examples) → JudgeScore` : macro, modèle
  gros (Claude Opus), appelé une fois en fin de run avec un résumé
  curaté + exemples sélectionnés par le per-row.

Pour ajouter un juge : créer `lib/judges/<name>.py`, implémenter au
moins `score_row`.

### Corpus

Un corpus vit sous `corpora/<slug>/` avec :
- `corpus.jsonl` : une ligne par sample, payload Deckle telemetry
  (`transcription_id`, `audio_file`, `text` = réf Whisper large-v3,
  `duration_seconds`, `tier`).
- `<audio_file>` : les WAV référencés dans corpus.jsonl.

**Les corpora ne sont jamais versionnés.** Pour en avoir un sur ta
machine, soit tu extrais depuis `%LOCALAPPDATA%\Deckle\telemetry\`,
soit tu en captures un nouveau via Deckle en mode télémétrie.

### Bench

Un **bench** est un scénario concret sous `benches/<scenario>/bench.py`.
Il assemble : un corpus, une ou plusieurs sources, des régimes de prompt,
des métriques, un juge. Sortie : `runs/<run-id>/results.jsonl`.

Pour ajouter un bench : créer `benches/<name>/` avec `bench.py` qui
importe les briques `lib/*` et orchestre. Voir `benches/voxtral-poc/`
en référence.

## Conventions de code

- **Encoding stdout** : forcer UTF-8 en début de script via
  `lib._base_compat._ensure_stdout_utf8()`. PowerShell est cp1252 par
  défaut, sinon les accents et box drawing chars (`─`) plantent avec
  `UnicodeEncodeError`.
- **Lazy imports** des dépendances lourdes (torch, anthropic, etc.) à
  l'instanciation, pas au top-level. Permet à un bench d'instancier
  une source A sans payer le coût d'import de la lib de la source B.
- **Sérialisation JSONL** : une ligne par row, écrite + flushed au fil.
  Si le bench crash, on a les rows déjà passés. Pas de buffering global.
- **Erreurs vs exceptions** : une transcription qui échoue renvoie un
  `Transcription(ok=False, error="...")`, **pas** une exception. Le
  bench écrit la row et continue. Une exception remonte le crash entier.
- **Docstrings** : style FR, prose en paragraphes courts, le **pourquoi**
  domine sur le **quoi**. Cohérent avec la doctrine `deckle-docs` du
  repo parent. Pas de docstring-cv ("This function does X.").

## Environnements Python

- `.venv-voxtral-dml/` : venv principal pour Voxtral via Transformers
  + torch-directml. `python312 -m venv .venv-voxtral-dml` puis
  `pip install torch torch-directml "transformers>=4.55,<5.0" mistral-common[audio] soundfile librosa jiwer anthropic`.
- `.venv-voxtral/` : ancien venv pour la stack llama.cpp (Phase 1/2),
  archivable.

Les deux sont gitignored (pattern `.venv*/`).

## Sécurité

- `benchmark/.env` contient `ANTHROPIC_API_KEY=...`. **Jamais commité**
  (pattern `*.env` dans .gitignore racine).
- Pour copier la clé sur ton portable : USB ou password manager, pas Git.
- En cas de fuite : révoquer via https://console.anthropic.com/settings/keys
  et en générer une nouvelle.
