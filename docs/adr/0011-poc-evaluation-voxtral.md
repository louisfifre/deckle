# ADR-0011 — POC d'évaluation Voxtral comme moteur de transcription

**Status** — proposed le 2026-05-23

Cet ADR met en mouvement la troisième condition de ré-évaluation listée par [ADR-0007](./0007-rester-sur-whisper-cpp-surveiller-voxtral.md) — « un benchmark Deckle réel montre que Voxtral Mini bat `whisper-large-v3` sur le couple WER/latence du corpus calibration personnel ». Si le POC tranche en faveur de Voxtral, cet ADR passera en `accepted` et superseder [ADR-0007](./0007-rester-sur-whisper-cpp-surveiller-voxtral.md). S'il tranche en faveur du statu quo, il passera en `rejected` et `ADR-0007` restera la doctrine en vigueur.

## Contexte

Trois forces convergent et justifient d'ouvrir une mesure formelle plutôt que de continuer en veille passive.

**Qualité française perçue.** Whisper, même en `large-v3`, produit régulièrement des sorties qui demandent une réécriture LLM substantielle pour devenir lisibles — accents oubliés, hallucinations sur les silences (chaîne fantôme « Sous-titrage Société Radio-Canada », crédits Amara.org), bouclage sur les segments lents. Voxtral est entraîné par Mistral avec un poids fort sur le français et porte une instruction-tuning native qui pourrait absorber une partie du travail aujourd'hui délégué à Ollama.

**Frustration opérationnelle.** Les boucles de transcription Whisper sur les dictées longues (palier `arrangement`, 600–1200 s) sont un irritant quotidien — la pipeline détecte et coupe les répétitions mais perd du contenu. Voxtral, basé sur l'architecture Mistral, devrait nativement éviter le pattern de bouclage Transformer décodeur observé sur Whisper.

**Émancipation Ollama.** Le pipeline actuel est Whisper → Ollama (Ministral 14B Q4) → clipboard. Trois processus, deux modèles chargés, une étape LLM lente. Si Voxtral instruction-tunable produit du texte propre directement (« lisse, fidèle, ponctué »), le pipeline se simplifie en Voxtral → clipboard. Plus court, moins de dépendances, plus aligné sur l'axe fondateur Deckle d'autonomie locale — un moteur Mistral local ASR+instruction au lieu d'un duo Whisper+Ollama.

**Curiosité dev.** Au-delà du résultat, monter le POC est l'occasion de comprendre la stack Transformers / `mistral-common[audio]`, le tokenizer audio Mistral, le wiring HuggingFace ↔ PyTorch ROCm sur Windows. Apprentissage indissociable de la décision technique selon les principes du projet.

L'infrastructure côté Deckle est déjà préparée pour la bascule : [ADR-0010](./0010-backend-asr-pluggable-via-iasrbackend.md) a refactoré `Deckle.Transcription` en parent backend-agnostique + child `Deckle.Transcription.Whisper`. Ajouter un second backend `Deckle.Transcription.Voxtral` ne demanderait plus de toucher à l'orchestrateur, juste de fournir une nouvelle implémentation `IAsrBackend`.

## Hypothèse principale

**Voxtral Mini 3B instruction-tuné peut produire, sur le corpus dictée personnel de Louis (français, registre courant et technique mêlés), une sortie texte de qualité supérieure à `whisper-large-v3` brut, sur un ou plusieurs de ces axes : fidélité linguistique, propreté (ponctuation/accents/registre), absence de bouclage, absence d'hallucinations connues.**

Hypothèse forte attachée : un régime instruction-tuné « lissé » de Voxtral peut produire un texte propre directement, réduisant ou supprimant la dépendance Ollama du pipeline aval.

## Options considérées

- **A. Continuer en veille passive sur les conditions de ré-évaluation de l'ADR-0007.** Pas de coût, pas de réponse non plus. La frustration et la curiosité ne se résolvent pas.
- **B. Bascule en aveugle vers Voxtral parce que « Mistral c'est mieux en français ».** Réfute la doctrine projet : pas de décision technique sans mesure. Risque d'investir trois semaines de chantier pour un gain qui n'existe pas, ou pire un régression sur un axe que personne n'avait mesuré.
- **C. POC mesuré en trois phases — manuel d'abord, élargi ensuite, autoresearch si la première phase est ambiguë.** Mécanique de décision lisible, coût borné en Phase 1 (un mini-corpus + 6 configs sur un harnais existant). Si la Phase 1 tranche net, on s'arrête. Si elle est ambiguë, on enrichit le corpus et on élargit la grille de prompts. Si l'optimum perceptuel est non trivial, on lance un loop autoresearch pour l'atteindre.

## Décision

Option C. Lancer un POC d'évaluation Voxtral mesuré, en trois phases.

**Phase 1 — Baseline manuelle de 6 configs sur un mini-corpus.** Une baseline Whisper (W0) et cinq configs Voxtral qui diffèrent uniquement par leur prompt système (V1 raw, V2 lissé, V3 fidèle, V4 fidèle annoté, V5 traduit EN). Le harnais est `benchmark/voxtral_bench.py`, calqué sur `_template_bench.py`. Le corpus de départ est l'audio warm-up déjà disponible plus tout audio sous `benchmark/corpus/voxtral-poc/` si présent. La sortie est un dump JSONL structuré sous `benchmark/runs/voxtral-poc-YYYY-MM-DD-HHMM/` plus un snapshot des configs/prompts utilisés.

**Phase 2 — Corpus élargi (conditionnelle).** Si la Phase 1 montre un signal clair mais sur trop peu d'échantillons, élargir le corpus dictée personnel (en attendant la fixation de la télémétrie corpus, chantier parallèle) et relancer la grille.

**Phase 3 — Loop autoresearch sur les prompts Voxtral (conditionnelle).** Si la Phase 1 ou 2 montre que la qualité Voxtral dépend fortement du prompt système et qu'aucune des cinq grandes familles ne sort nettement gagnante, lancer un loop autoresearch piloté par le harnais existant pour explorer l'espace des prompts et trouver l'optimum local sur le corpus.

Le bench produit les outputs structurés (JSONL + snapshots des configs). Le rapport humain de décision n'est pas généré par le bench — il est rédigé manuellement (Claude Opus 4.7 en session Claude Code) en lisant les JSONL. Cette séparation est délibérée — le bench ne tranche pas, il instrumente la donnée pour permettre une décision humaine éclairée.

**Méthode de scoring.** Chaque sortie est scorée sur deux étages. Un étage objectif rule-based en parallèle de l'exécution — RTF (Real-Time Factor), taux de bouclage (regex n-grammes répétés ≥ 2× sur fenêtres glissantes), taux d'hallucinations connues (regex sur leak prompt Deckle « .NET, Visual Studio, Python, Whisper » et sur chaînes type « Sous-titrage Société Radio-Canada »). Un étage qualitatif via un judge LLM local — Ollama hébergeant `ministral-3:14b` (équivalent fonctionnel à Ministral 14B) avec un prompt judge dédié, notant 0–100 sur les axes fidélité, propreté, absence de leak/halluc, régime respecté. L'arbitrage final est humain — lecture des sorties et du scoring agrégé par Claude Opus en session, puis décision tranchée par Louis.

**Critère de décision Voxtral retenu.** Voxtral Mini gagne sur le corpus de calibration sur au moins deux des trois axes (fidélité, propreté, absence de bouclage/hallucinations) avec un RTF acceptable sur la machine cible (à instrumenter — seuil exact à fixer après mesure de la baseline). En cas d'arbitrage proche, le critère subjectif de qualité française perçue (lecture humaine) tranche.

## Conséquences

**Pendant le POC.** Aucune modification du code Deckle (`src/`). Tout vit sous `benchmark/` et `docs/adr/`. L'environnement Python pour Voxtral est isolé sous `benchmark/.venv-voxtral/` (gitignored). Un script `benchmark/setup-voxtral-env.ps1` reproduit l'env from scratch pour les recréations futures. Les commits suivent la doctrine `deckle-commits` (Conventional Commits, pas de trailer LLM, scopes adaptés). La branche du POC est `poc/voxtral-evaluation`, dans un worktree dédié `D:/worktrees/deckle/voxtral-evaluation/`.

**Si Voxtral gagne (verdict accepted).** Cet ADR passe en `accepted`. L'ADR-0007 passe en `superseded le YYYY-MM-DD par [ADR-0011]`. Un chantier d'intégration s'ouvre : extension de `IAsrBackend` si nécessaire pour absorber les spécificités Voxtral (notamment la dimension instruction-tuning si on l'utilise), création du module enfant `Deckle.Transcription.Voxtral`, intégration d'un runtime Python embedded, lifecycle du process Python (cold start, warm pool, fault recovery), setting `Engine = Whisper | Voxtral` exposé en UI Settings. La couche P/Invoke whisper.cpp reste — le moteur Whisper devient une option, pas un héritage à supprimer. Une ré-évaluation séparée tranchera ultérieurement si on retire Whisper du shipping ou si on garde les deux moteurs en option.

**Si Voxtral perd (verdict rejected).** Cet ADR passe en `rejected`. ADR-0007 reste accepted, doctrine inchangée. Le harnais `voxtral_bench.py` et l'env Python isolé restent dans `benchmark/` comme matériau de re-mesure ultérieure — la décision est valide à la date du POC, pas pour toujours. Voxtral retombe en veille passive selon les conditions inchangées de l'ADR-0007.

**Pendant la mesure, ce qui devient impossible.** Toucher au code de `src/Deckle.Transcription/` ou à un autre module Deckle sous prétexte d'optimisation Whisper pendant le POC — cela rendrait toute comparaison ininterprétable. Le baseline Whisper W0 doit être figé sur la version `main` au moment du POC.
