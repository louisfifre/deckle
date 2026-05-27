---
name: research-asr-benchmarks-voxtral-vs-whisper-fr-2026-05-27
description: "Comparaison ASR français au 2026-05-27 entre Voxtral Mini 3B / Small 24B, Whisper large-v3, Phi-4-Multimodal et alternatives. Tableaux WER sur FLEURS, CommonVoice, MLS, lecture du mode d'échec TTS-overfitting."
type: research
---

# Comparaison ASR français — Voxtral, Whisper, Phi-4 et alternatives

> Source : sub-agent recherche externe (general-purpose) lancé en session 2026-05-27, triangulé entre paper Voxtral arXiv 2507.13264, paper Phi-4-Mini arXiv 2503.01743, ASR Leaderboard indépendant arXiv 2510.06961v4 (mars 2026).

## Context

À ouvrir comme référence quand on compare des modèles ASR FR pour Deckle. Au 2026-05-27, Voxtral Mini 3B BF16 est le candidat POC en cours (worktree `voxtral-bf16-poc`). Cette synthèse cadre où il se situe vs Whisper large-v3 (production actuelle de Deckle), Voxtral Small 24B (hors budget GPU 20 GiB), et Phi-4-Multimodal (candidat ONNX/DirectML).

## Tableau comparatif WER FR (sources triangulées)

WER en pourcentage (plus bas = meilleur).

| Modèle | FLEURS fr | CommonVoice fr | MLS fr | Source |
|---|---|---|---|---|
| Whisper large-v3 | 5.55 | 11.33 | 5.09 | Voxtral paper |
| Whisper large-v3 (leaderboard indé) | 6.36 | — | — | ASR Leaderboard 2026 |
| Voxtral Mini 3B | 4.87 | 8.92 | 5.28 | Voxtral paper |
| Voxtral Mini Transcribe | 4.22 | 7.29 | 4.14 | Voxtral paper |
| Voxtral Small 24B | **4.03** | **6.18** | **3.73** | Voxtral paper |
| Voxtral Small 24B (leaderboard indé) | 4.13 | — | — | ASR Leaderboard 2026 |
| Phi-4-multimodal-instruct | 4.35 | 8.08 | — | Phi-4 paper |
| Phi-4-multimodal (leaderboard indé) | 5.20 | — | — | ASR Leaderboard 2026 |
| SeamlessM4T-v2-Large | 7.40 | 9.75 | — | Phi-4 paper |
| ElevenLabs Scribe | 5.07 | 5.44 | 5.80 | Voxtral paper |

Triangulation : sur FLEURS-fr, l'ASR Leaderboard indépendant confirme Voxtral Small ≈ 4.13 (vs 4.03 annoncé) et place Whisper large-v3 à 6.36 (vs 5.55 annoncé), donc **l'écart Voxtral > Whisper est plus large que ce que Mistral revendique**. Voxtral Mini n'est pas dans le leaderboard indé.

## Verdict Voxtral Mini 3B vs Whisper large-v3 (FR pur)

Voxtral Mini bat Whisper large-v3 sur les trois datasets FR avec des marges nettes. FLEURS : 4.87 vs 5.55 (−12 %). CommonVoice : 8.92 vs 11.33 (−21 %). MLS : 5.28 vs 5.09 (+4 %, Whisper marginalement devant sur livres audio lus). L'écart est plus prononcé sur CommonVoice (parlé varié, accents) que sur MLS (lecture studio), ce qui contredit légèrement l'inquiétude du paper Voxtral sur la dégradation au parlé spontané. **En transcription FR pure, Voxtral Mini 3B est globalement supérieur à Whisper large-v3 d'environ 10-20 % de WER relatif**, sauf marginalement sur la lecture studio MLS.

Le variant `Voxtral Mini Transcribe` (mode dédié transcription sans capacités conversationnelles, accessible via le mode transcribe de l'API Mistral) gagne encore ~0.6-1.5 point de WER absolu sur Mini standard. Comportement à valider en local : non documenté côté model card HF.

## Voxtral Small 24B — vaut-il les 24 GiB INT8 ?

Sur les benchmarks publics, Voxtral Small est uniformément en tête : 4.03 / 6.18 / 3.73 — il prend ~0.8 point absolu de WER au Mini sur FLEURS et MLS, ~2.7 points sur CommonVoice. L'écart relatif au Mini est de 14-31 % selon le dataset, donc réel mais pas massif. Côté VRAM : BF16 = ~55 GiB (model card HF, hors marge KV-cache), inaccessible sur 20 GiB. INT8 ≈ 27-30 GiB en pratique (encodeur Whisper 640M + Mistral Small 24B + activations + KV-cache pour audio long), encore au-dessus du budget. INT4 (AWQ/GPTQ) ≈ 13-15 GiB serait jouable mais la quantization 4-bit dégrade typiquement les têtes de transcription sur langues non-anglaises de 0.3-1 point de WER — pas de mesure publique disponible. Le support GPTQ/AWQ Voxtral est en cours d'ajout côté vLLM, pas encore stabilisé. **Verdict** : pour 20 GiB, Voxtral Small en INT4 *peut* tenir mais avec un risque de dégradation non mesuré qui rapprocherait du Mini en BF16. **Voxtral Mini 3B en BF16 (~9.5 GiB) reste le sweet spot rationnel.**

## Phi-4-Multimodal-Instruct

Performances FR très proches de Voxtral Mini selon le paper Microsoft : 4.35 sur FLEURS, 8.08 sur CommonVoice — bat Whisper large-v3 et SeamlessM4T-v2. L'ASR Leaderboard indépendant donne 5.20 sur FLEURS-fr, soit nettement moins bon que les chiffres Microsoft et derrière Voxtral Small (4.13). 5.6B params, BF16 ≈ ~13 GiB. Écosystème : HF Transformers OK, ONNX Runtime supporté nativement par Microsoft (avantage majeur vs Voxtral), vLLM partiel. Sur ROCm/RX 7900 XT, Phi-4 Transformers fonctionne ; ONNX DirectML/ROCm est un chemin alternatif intéressant pour découpler du Python. **Verdict** : qualité FR comparable à Voxtral Mini mais ni clairement devant ni derrière selon la source, écosystème ONNX un vrai plus pour une app Windows.

Voir la note de recherche dédiée [research--phi-4-multimodal-state-of-the-art--2026-05-27.md](research--phi-4-multimodal-state-of-the-art--2026-05-27.md) pour la cartographie complète.

## Mode d'échec « TTS overfitting » — peu de mesures publiques

Le paper Voxtral admet la limite mais ne publie pas de benchmark dédié read-speech vs spontané. Les datasets standards utilisés (FLEURS, CommonVoice, MLS) sont tous majoritairement de la lecture ou du parlé semi-structuré — ils ne stressent pas la généralisation au conversationnel bruité. Une seule source indépendante creuse le sujet : **Scale AI « Voice Showdown »** (lancement mentionné dans la presse VentureBeat) prétend être le premier benchmark voice AI sur audio humain réel (accents, bruit de fond, phrases inachevées, filler), où les modèles open-weight incluant Voxtral Small *« trail significantly »*. Aucun chiffre FR public extrait. L'ASR Leaderboard 2026 distingue les styles de datasets (CoVoST-2 lu vs earnings calls spontané) mais ne publie pas de tableau croisé style × langue. **Bilan honnête** : la limite est admise par les auteurs et corroborée qualitativement par Scale AI, mais aucun chiffre public FR ne quantifie la dégradation Voxtral sur audio spontané humain. Pour le POC Deckle, un mini-benchmark interne sur des dictées réelles (pas du TTS, pas du Common Voice studio) est plus informatif que toute source externe disponible aujourd'hui — c'est ce que le corpus `voxtral-val-30` construit.

## Bilan opérationnel

Pour 20 GiB VRAM sur RX 7900 XT, le terrain plausible est **Voxtral Mini 3B BF16** (qualité supérieure à Whisper large-v3, ~9.5 GiB, ROCm OK) ou **Phi-4-multimodal BF16** (qualité comparable, ~13 GiB, atout ONNX pour Windows). Voxtral Small reste hors budget sans INT4 risqué. Whisper large-v3 conserve un atout : maturité whisper.cpp, ONNX, DirectML — si la stack actuelle pose problème, la régression de qualité reste mesurée (~12 % WER relatif) et le gain de simplicité opérationnelle peut compenser. Sur l'audio spontané réel, aucun chiffre public ne tranche : **benchmark interne nécessaire**.

## Sources

- [Voxtral paper (arXiv 2507.13264)](https://arxiv.org/html/2507.13264v1)
- [Voxtral Mini 3B model card](https://huggingface.co/mistralai/Voxtral-Mini-3B-2507)
- [Voxtral Small 24B model card](https://huggingface.co/mistralai/Voxtral-Small-24B-2507)
- [Phi-4-Mini Technical Report (arXiv 2503.01743)](https://arxiv.org/html/2503.01743v1)
- [Phi-4-multimodal-instruct model card](https://huggingface.co/microsoft/Phi-4-multimodal-instruct)
- [ASR Leaderboard paper (arXiv 2510.06961)](https://arxiv.org/html/2510.06961v4)
- [Open ASR Leaderboard HF](https://huggingface.co/spaces/hf-audio/open_asr_leaderboard)
- [vLLM issue Voxtral quantization](https://github.com/vllm-project/vllm/issues/38235)
- [Scale AI Voice Showdown (VentureBeat)](https://venturebeat.com/data/scale-ai-launches-voice-showdown-the-first-real-world-benchmark-for-voice-ai)
