# ADR-0007 — Rester sur whisper.cpp, surveiller Voxtral pour V2

**Status** — accepted le 2026-05-23

## Contexte

Le moteur de transcription actuel de Deckle est `whisper.cpp` consommé via P/Invoke depuis `Deckle.Transcription`. Stable, performant sur Vulkan, pile native intégrée par le pipeline de provisioning. La question d'une bascule vers Voxtral (famille voix Mistral) revient régulièrement, en particulier depuis la sortie de Voxtral Mini 4B Realtime sous Apache 2.0 — variante légère qui rentre dans le budget VRAM d'un poste personnel.

Voxtral est un ASR pur (pas de fusion speech+LLM), comme Whisper. Le pipeline réécriture de Deckle resterait découplé en tout état de cause. La question se limite à la couche transcription.

État de la compatibilité moteurs au 2026-05-23 :

- **llama.cpp** porte un GGUF `Voxtral-Mini-3B-2507-GGUF` qui tourne en batch. Le streaming temps réel est bloqué dans [ggml-org/llama.cpp#20914](https://github.com/ggml-org/llama.cpp/pull/20914), Phase 1 planification, rien mergé. Le support `libmtmd` (tokenizer audio Mistral) existe en communauté mais sans API publique stabilisée.
- **Ollama** n'est pas intégré. [ollama/ollama#12440](https://github.com/ollama/ollama/issues/12440) et [#11432](https://github.com/ollama/ollama/issues/11432) sans jalons. Les mainteneurs ne priorisent pas les modèles audio.
- **vLLM** reste bloqué Linux par toolchain CUDA+Triton. Des forks Windows communautaires existent, non officiels.
- **HuggingFace Transformers ≥ 5.2.0** porte Voxtral officiellement, tourne sur Windows CUDA ou ROCm natif. Chemin le plus stable aujourd'hui, mais impose un runtime Python — incompatible avec la philosophie native .NET de Deckle.
- **`antirez/voxtral.c`** est un moteur d'inférence Voxtral en C pur sans dépendance, créé par antirez (Redis). Pas de streaming API, early stage — mais pattern architectural identique à `whisper.cpp` (compilable DLL → P/Invoke .NET). Candidat naturel pour intégration Deckle-native quand suffisamment mature.
- **`whisper.cpp` lui-même** a une issue Voxtral [#3326](https://github.com/ggml-org/whisper.cpp/issues/3326) sans implémentation. Dead end.

La couche P/Invoke actuelle (`WhisperPInvoke.cs`, `WhisperStructs.cs`, `WhisperParamsMapper.cs`) est câblée sur l'API `whisper.cpp` — incompatible avec Voxtral sans réécriture large de `WhispEngine.cs`. `Deckle.Audio` reste intact (PCM 16 kHz mono). La couche App/HUD/`IWhispEngineHost` ne bouge pas.

## Options considérées

- **A. Basculer maintenant via HuggingFace Transformers Python.** Mature, supporté officiellement, Windows-friendly. Impose un runtime Python embedded — rupture avec la doctrine native .NET du projet. Coût d'intégration élevé pour un bénéfice perceptuel incertain.
- **B. Attendre llama.cpp/Ollama et basculer dès qu'un streaming temps réel arrive.** Voie naturelle pour Deckle parce que la pile P/Invoke est analogue. Échéance non prévisible — les deux issues stagnent.
- **C. Suivre `antirez/voxtral.c` jusqu'à maturité.** Architecture la plus proche de `whisper.cpp`, intégration P/Invoke triviale par mimétisme avec l'existant. Pas de streaming aujourd'hui ; pas de release stable. Veille passive.
- **D. Rester sur `whisper.cpp` et différer la question.** Pas de coût immédiat, pas de churn. Le risque est de stagner sur un moteur potentiellement dépassé.

## Décision

Option D retenue. Deckle reste sur `whisper.cpp` pour la V1. Voxtral est mis en veille active via deux balises de surveillance — `antirez/voxtral.c` côté implémentation native, et le streaming `llama.cpp` côté écosystème mainstream. Aucun engagement de bascule ; ré-évaluation déclenchée par les conditions ci-dessous.

## Conséquences

Le pipeline transcription reste stable, ce qui libère l'effort pour les chantiers ouverts (refonte observabilité, ambient lighting, refonte UI Settings). Pas de coût d'intégration immédiat.

La couche P/Invoke `whisper.cpp` continue d'accumuler des spécificités (filtrage de répétitions, `entropy_thold` natif, `new_segment_callback`) — toute bascule future demandera un module `Deckle.Voxtral` parallèle plutôt qu'une mutation in-place de `Deckle.Transcription`. Routage via setting « moteur de transcription » assumé comme la voie d'intégration le jour venu.

Conditions de ré-évaluation : `antirez/voxtral.c` publie une release stable avec API streaming et build Windows reproductible ; OU `llama.cpp` merge le streaming temps réel Voxtral et publie un binaire Windows officiel ; OU un benchmark Deckle réel montre que Voxtral Mini bat `whisper-large-v3` sur le couple WER/latence du corpus calibration personnel. Tant qu'aucune de ces trois conditions n'est remplie, on reste.
