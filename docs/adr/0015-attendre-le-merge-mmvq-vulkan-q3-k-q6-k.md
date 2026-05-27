# ADR-0015 — Attendre le merge upstream de MMVQ Vulkan pour Q3_K/Q6_K

**Status** — accepted le 2026-05-26

Cet ADR documente une découverte faite lors du POC Voxtral via llama.cpp Vulkan ([ADR-0014](./0014-poc-evaluation-voxtral.md)) et tranche sur la posture à adopter : pas de patch local, veille passive sur le merge upstream.

## Contexte

`ggml/src/ggml-vulkan/ggml-vulkan.cpp` ligne 7988 désactive explicitement le chemin **MMVQ** (Mat-Mul-Vector-Quantized) pour les types Q3_K et Q6_K parce que leurs blocs ne sont alignés que sur 2 bytes, alors que MMVQ exige un alignment 32-bit. Le chemin de fallback (matmul scalaire) est nettement plus lent, mais le commentaire dans le code indique que MMVQ posait des problèmes de perf sur certaines machines avec ces types — d'où la désactivation conservatrice.

Deux PRs OPEN sur `ggml-org/llama.cpp` proposent de lever cette limitation :

- **[#22951](https://github.com/ggml-org/llama.cpp/pull/22951) — « vulkan: Pad Q3_K/Q6_K tensors out to 32-bit alignment »** (créée 2026-05-11). Padde les blocs à 32 bits, coût ~1.8% sur Q3_K et 0.9% sur Q6_K, gain mesuré +150% sur Qwen3.5-9B Q3_K pur et +174% sur Q6_K pur, sur Intel Battlemage / mesa.
- **[#23056](https://github.com/ggml-org/llama.cpp/pull/23056) — « vulkan: Block-load Q3_K/Q6_K block data and subtract on 32b ints »** (créée 2026-05-14). Partie sans-padding de #22951, isole le switch MMVQ + block-load. +57% Q3_K et +78% Q6_K via MMVQ, +24% et +48% supplémentaires via block-load.

Les gains spectaculaires sont relevés sur des quants **purs** Q3_K ou Q6_K, peu fréquents dans la pratique. Sur un quant mixte type Q4_K_XL (qui ne contient qu'une minorité de Q6_K pour les couches d'embedding/output), l'auteur observe lui-même « an extra token per second » — gain marginal de l'ordre du pourcent.

Deux dimensions de pertinence pour Deckle. **Stack courante.** Le POC Voxtral mesure du `Voxtral 24B Q4_K_M` sur RX 7900 XT via Vulkan — quant majoritairement Q4_K avec une fraction Q6_K, le gain attendu après merge se compte donc en quelques pourcents de tokens/s, pas en +166%. Les benchmarks Battlemage/mesa ne transposent pas mécaniquement sur AMD RDNA3 — la nature du gain (kernel MMVQ activé sur tensors qui en étaient privés) suggère un signe positif, mais l'ampleur reste à mesurer. **Stacks futures.** Si Deckle bascule vers un quant plus agressif (Q3_K_S/M pour réduire le footprint VRAM des grosses ASR multimodales) ou si un futur backend GGUF de whisper.cpp utilise Q6_K pur pour ses embeddings, ces deux PRs deviennent critiques — facteur 2 à 3 sur le throughput de ces tensors.

## Options considérées

- **A. Forker `llama.cpp` localement, appliquer les patchs #22951 + #23056 sur la copie Deckle.** Mauvaise option en l'état. Coût de maintenance d'un fork upstream pour un gain de quelques pourcents sur la stack actuelle. Le patch est en cours de review active (mise à jour #23056 le 2026-05-22), il sera mergé.
- **B. Veille passive, attendre le merge upstream, intégrer au prochain rebuild de la lib native Deckle.** Aucun coût immédiat, le bénéfice arrive gratuitement quand on tire un `libllama.dll` post-merge. La discipline de rebuild des runtimes natifs Deckle est déjà documentée dans [docs/reference/reference--native-runtime--1.0.md](../reference/reference--native-runtime--1.0.md).
- **C. Ignorer purement le finding.** Rejette une donnée mesurable et oublie un gain potentiel non négligeable sur les quants futurs. Mauvaise hygiène doctrinale.

## Décision

Option B. Veille passive sur le merge upstream des PRs [#22951](https://github.com/ggml-org/llama.cpp/pull/22951) et [#23056](https://github.com/ggml-org/llama.cpp/pull/23056). Au prochain rebuild de `libllama.dll` qui inclura ces patchs, mesurer le delta sur le bench Voxtral 24B Q4_K_M pour quantifier l'effet concret sur la stack Deckle.

## Conséquences

**Maintenant.** Aucune modification de code, aucun fork, aucun patch local appliqué. Le `voxtral_llamacpp.py` du bench tourne sur la build standard de `llama.cpp` à son dernier tag stable au moment du POC.

**Au merge upstream.** Repasser une session de bench Voxtral 24B Q4_K_M avant/après upgrade `libllama.dll`, sur un sous-ensemble fixe du corpus (typiquement les buckets durée moyens où le RTF est le plus lisible), pour quantifier le gain concret sur AMD RDNA3 + Q4_K_M mixte. Si le gain est ≥ 5%, mise à jour `libllama.dll` validée comme upgrade utile ; sinon, juste une amélioration cosmétique sans condition particulière.

**Conditions de réévaluation.** Si Deckle envisage de basculer Voxtral (ou tout autre modèle GGUF) vers un quant **Q3_K_S, Q3_K_M ou Q6_K pur** pour ses tensors principaux, le gain attendu passe de quelques pourcents à un facteur 1.5–2.7×. Dans ce cas, accélérer le passage à un `libllama.dll` post-merge devient prioritaire, voire (si les PRs traînent) justifier un patch local temporaire.
