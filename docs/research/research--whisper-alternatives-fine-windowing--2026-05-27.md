---
name: research-whisper-alternatives-fine-windowing-2026-05-27
description: "Alternatives à whisper.cpp pour réduire la fenêtre d'inférence Whisper de 30s à 5-10s en gardant une intégration .NET et un GPU AMD. Le levier audio_ctx documenté, comparatif des candidats."
type: research
---

# Whisper alternatives pour windowing fin sur AMD/.NET — synthèse 2026-05-27

> Source : sub-agent recherche externe (general-purpose) lancé en session 2026-05-27, sources triangulées (issues GitHub whisper.cpp, doc faster-whisper, doc HF Transformers, paper Simul-Whisper arXiv 2406.10052, paper Apple ML arXiv 2505.23627).

## Context

Deckle utilise aujourd'hui `whisper.cpp` via P/Invoke depuis C# (binaires `libwhisper.dll`, `ggml-vulkan.dll` dans `%LOCALAPPDATA%\Deckle\native\`). Limitation observée : Whisper traite par chunks de 30 s, ce qui impose une latence minimale incompressible. Pour une hotkey de dictée à latence faible, l'objectif est de **descendre à des fenêtres de 5-10 s configurables** sans dégrader la qualité large-v3.

## Découverte centrale qui change le cadrage

**La contrainte « 30 secondes » n'est pas une fatalité de whisper.cpp.** Le paramètre `whisper_full_params.audio_ctx` permet déjà de réduire la fenêtre d'encodeur sous les 30 s. Formule empirique documentée dans la discussion whisper.cpp #297 : `audio_ctx = (audio_length/30) * 1500 + 128`, valeur arrondie à un multiple de 64. Pour 5-10 s, cela donne ~256-512. Gain rapporté : jusqu'à 3× sur des clips courts. **Le sujet n'est donc pas de quitter whisper.cpp, mais de l'exposer correctement depuis le wrapper C#.**

Point d'attention documenté : le décodeur Whisper a été entraîné sur 30 s ; à `audio_ctx` trop réduit, on observe des **répétitions infinies de tokens en fin**. Le seuil exact dépend du modèle, à benchmarker.

## Tableau comparatif

| Candidat | Windowing min | Support AMD | Intégration .NET | Qualité large-v3 |
|---|---|---|---|---|
| **whisper.cpp + `audio_ctx`** | ~5 s via `audio_ctx≈256` (à valider) | Vulkan natif, HIP/ROCm Linux | P/Invoke direct (en place) | Préservée (même modèle) |
| **Whisper.net** | Idem whisper.cpp si exposé | `Whisper.net.Runtime.Vulkan` packagé | Binding C# officiel, NuGet | Préservée |
| **Faster-Whisper / CTranslate2** | `chunk_length` non fiable en large-v3 (issues #624, #428) | CTranslate2 **n'a pas de backend ROCm** ni Vulkan, CUDA only | Pas de binding C# officiel ; Soenneker wrap un EXE | Préservée |
| **HF Transformers + `chunk_length_s`** | Configurable, optimal 30 s pour large-v3, 25 s pour distil-large-v3 | PyTorch+ROCm sur Linux, DirectML possible mais immature | Sous-process Python obligatoire | Préservée |
| **ONNX Runtime + DirectML** | À implémenter manuellement | DirectML couvre AMD DX12 | C# natif via `Microsoft.ML.OnnxRuntime.DirectML` | Préservée, mais issue Olive #1213 signale `DecoderMaskedMultiHeadAttention` NOT_IMPLEMENTED — non trivial |
| **insanely-fast-whisper** | `chunk_length_s` configurable | CUDA only ; fork `insanely-fast-whisper-rocm` existe (Linux) | Python sous-process | Préservée |
| **WhisperX** | VAD-driven, fenêtres ~30 s avec coupures sur silence | Hérite de faster-whisper, donc pas d'AMD natif (issue #566 ouverte) | Python sous-process | Préservée + alignement |
| **Distil-Whisper** | Identique à HF | Idem HF | Idem HF | ~1 % WER en plus, parfois mieux en long-form (moins d'hallucinations) |
| **Simul-Whisper** | Chunks 1 s, +1.46 % WER | N/A — politique de décodage, pas un runtime | Pas de binding | Dégradation mesurée |

## Recommandation

**Rester sur whisper.cpp via Whisper.net (ou le wrapper P/Invoke actuel), et exposer `audio_ctx` à 256-512 pour les fenêtres 5-10 s.** C'est la seule voie qui coche les quatre cases simultanément : windowing configurable, GPU AMD via Vulkan déjà supporté (`Whisper.net.Runtime.Vulkan`), intégration .NET native, qualité large-v3 préservée.

Toutes les alternatives sérieuses au windowing fin (HF Transformers, insanely-fast-whisper, WhisperX) imposent **Python en sous-process**, ce qui ajoute une dépendance lourde, une couche IPC, et un coût de démarrage incompatible avec une hotkey de dictée à faible latence. CTranslate2 (faster-whisper) est éliminé de fait : pas de backend AMD, et le `chunk_length` est explicitement cassé sur large-v3.

ONNX Runtime + DirectML est théoriquement séduisant (C# natif, AMD via DX12) mais l'écosystème Whisper-ONNX a des trous documentés (opérateurs custom de Whisper mal supportés), et le windowing n'est pas paramétrable hors-de-la-boîte — il faudrait réimplémenter la boucle d'encodage. À retenir comme piste long terme si Vulkan plafonne, pas comme cible immédiate.

## Trade-offs explicites

**whisper.cpp + `audio_ctx` réduit** — le gain de latence vient à un coût de qualité non quantifié dans la doctrine publiée. Le décodeur peut « glitcher » (répétitions) si la valeur s'éloigne trop de l'entraînement 30 s. Le risque doit être benchmarké sur le corpus Deckle aux paliers 256/384/512.

**Si la qualité dégrade trop à 5 s** — passer à **Distil-Whisper-large-v3** via Whisper.net (le format GGML est supporté). Coût : +1 % WER nominal sur short-form, parfois meilleur en long-form. Bénéfice : 6.3× plus rapide, ce qui peut autoriser des fenêtres plus longues à latence équivalente — autre façon de résoudre le problème.

**Si on a besoin de vrai streaming continu** (pas juste hotkey one-shot) — la stratégie WhisperX/Silero-VAD est documentée (380-520ms end-to-end), mais elle impose le sous-process Python. Garder en réserve pour un module futur (AskHud temps réel) plutôt que pour la dictée hotkey.

## Risques

**Seuil de dégradation à `audio_ctx` faible** — documenté qualitativement (répétitions de tokens), pas chiffré dans les sources consultées. Le papier Simul-Whisper donne un repère indirect : chunk 1 s = +1.46 % WER, ce qui suggère qu'à 5 s la dégradation devrait rester modeste, mais c'est avec une politique de décodage adaptée, pas un simple `audio_ctx` réduit. **À benchmarker localement.**

**Hallucinations sur silences en fin de fenêtre courte** — risque amplifié à fenêtre courte (le « Thank you for watching » fantôme). Mitigation connue : VAD en amont (Silero-VAD, déjà léger, intégrable côté C#).

**`Whisper.net.Runtime.Vulkan` sur RX 7900 XT Windows** — issue #2867 ouverte sur whisper-stream qui exit silencieusement avec cette config précise. À vérifier sur la machine cible avant de bâtir dessus ; un canal de repli HIP/CPU doit rester possible.

## Sources

- [faster-whisper issue #624 — chunk_length cassé sur large-v3](https://github.com/SYSTRAN/faster-whisper/issues/624)
- [faster-whisper issue #428 — contrôle du chunk_length](https://github.com/SYSTRAN/faster-whisper/issues/428)
- [whisper.cpp discussion #297 — audio_ctx documenté](https://github.com/ggml-org/whisper.cpp/discussions/297)
- [whisper.cpp issue #1855 — audio_ctx variable, 3x speedup](https://github.com/ggerganov/whisper.cpp/issues/1855)
- [whisper.cpp issue #2867 — Vulkan whisper-stream exits AMD 7900 XT Windows](https://github.com/ggml-org/whisper.cpp/issues/2867)
- [whisper.cpp discussion #206 — chunking long audio](https://github.com/ggml-org/whisper.cpp/discussions/206)
- [Whisper.net officiel](https://github.com/sandrohanea/whisper.net)
- [Whisper.net.Runtime.Vulkan NuGet](https://www.nuget.org/packages/Whisper.net.Runtime.Vulkan/)
- [Distil-Whisper officiel — WER large-v3](https://github.com/huggingface/distil-whisper)
- [distil-large-v3 HF model card](https://huggingface.co/distil-whisper/distil-large-v3)
- [Simul-Whisper arXiv 2406.10052 — +1.46% WER à 1s](https://arxiv.org/abs/2406.10052)
- [WhisperX paper — VAD chunking](https://arxiv.org/html/2303.00747v2)
- [HF Transformers Whisper — chunk_length_s](https://huggingface.co/docs/transformers/en/model_doc/whisper)
- [ROCm blog — Whisper sur AMD GPU](https://rocm.blogs.amd.com/artificial-intelligence/whisper/README.html)
- [whisperX issue #566 — ROCm AMD encore ouvert](https://github.com/m-bain/whisperX/issues/566)
- [Olive issue #1213 — Whisper DirectML opérateur manquant](https://github.com/microsoft/Olive/issues/1213)
- [AMD GPUOpen — ONNX DirectML EP guide](https://gpuopen.com/learn/onnx-directlml-execution-provider-guide-part1/)
- [insanely-fast-whisper-rocm fork](https://github.com/beecave-homelab/insanely-fast-whisper-rocm)
- [openai/whisper discussion #679 — solutions hallucination](https://github.com/openai/whisper/discussions/679)
