# ADR-0008 — Rester sur Vulkan pour les backends GPU natifs

**Status** — accepted le 2026-05-23

## Contexte

Les modules natifs de Deckle qui exploitent le GPU (whisper.cpp pour la transcription, Ollama pour la réécriture LLM) tournent aujourd'hui sur backend Vulkan. La pile est stable, les DLLs natives whisper.cpp + backends ggml-vulkan sont compilées et bundlées, et la recette de recompilation vit dans [docs/reference/reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md).

AMD a livré ROCm 7.2.x stable Windows au CES janvier 2026, couvrant RX 9070/9060 (RDNA 4), RX 7900 XTX/7700 (RDNA 3), et PRO W7900. La question d'une bascule ROCm Windows revient à intervalle régulier, motivée par les gains de performance annoncés sur token generation côté LLM.

État au 2026-04-30, ré-évalué au 2026-05-23 sans changement substantiel :

- **whisper.cpp ROCm Windows reste pré-prod.** Bug [ggml-org/whisper.cpp#1453](https://github.com/ggml-org/whisper.cpp/issues/1453) produit du texte corrompu en sortie. Pas de build HIP Windows officiel dans le repo principal. L'audio est le cœur du produit Deckle — la fiabilité n'est pas négociable.
- **Ollama ROCm a une régression RDNA4 active.** [ollama/ollama#14686](https://github.com/ollama/ollama/issues/14686) — `gfx1201` filtré au boot Windows 11, fallback CPU silencieux. Sans correctif, RDNA4 est inutilisable côté LLM avec ROCm.
- **Gain de performance inégal.** ROCm bat Vulkan sur token generation. Vulkan bat ROCm sur prompt processing. Pour la réécriture Deckle (gros prompts, sortie courte), Vulkan reste compétitif.
- **Coût d'intégration élevé.** Recompiler whisper.cpp avec backend HIP, redéployer les DLLs natives, tester sur variantes GPU. Le bundle natif passe par la release `native-vX.Y.Z` du repo Deckle — multiplier les targets revient à multiplier les builds.

La référence GPU exacte du poste de Louis n'est pas confirmée. RDNA3 vs RDNA4 change le verdict (RDNA4 = bug Ollama bloquant).

## Options considérées

- **A. Basculer sur ROCm Windows maintenant.** Gain de performance LLM token-gen, alignement avec la roadmap GPU AMD long terme. Bloqué par la corruption whisper.cpp et la régression Ollama RDNA4 — non viable.
- **B. Maintenir Vulkan, ré-évaluer périodiquement.** Status quo. Stable, déjà intégré, performance acceptable. Risque : rester sur un backend potentiellement dépassé.
- **C. Architecture multi-backend (Vulkan + ROCm sélectionnable).** Flexibilité maximale, coût d'intégration et de test multiplié par deux. Disproportionné à ce stade du projet.

## Décision

Option B retenue. Deckle reste sur Vulkan pour whisper.cpp et Ollama. Aucune bascule ROCm avant que les conditions de ré-évaluation soient remplies.

## Conséquences

Le pipeline natif reste stable et reproductible. La recette de recompilation `reference--native-runtime--1.0.md` continue de suffire, sans branche HIP à maintenir en parallèle.

Pas d'accès aux gains ROCm sur token generation. Acceptable parce que la réécriture LLM dans Deckle est asynchrone (l'utilisateur attend déjà 1-3 s en moyenne sur Vulkan, gagner 200 ms n'est pas un seuil de friction perçue) et parce que le pipeline transcription est bornée par whisper.cpp lui-même, pas par le backend GPU à l'exception du tout premier modèle.

Conditions de ré-évaluation, à vérifier en cumul : `ollama/ollama#14686` fermé ET retours stabilité Windows 11 RDNA4 publiés (vise été 2026) ; whisper.cpp publie un build HIP Windows officiel dans le repo principal ; un benchmark Deckle réel sur le GPU effectif du poste de Louis montre **>20%** de gain net sur le pipeline complet (transcription + réécriture). Préalable absolu à tout test : confirmer la référence GPU via `dxdiag` ou Settings → System → Display.
