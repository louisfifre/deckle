---
name: research-phi-4-multimodal-state-of-the-art-2026-05-27
description: "Synthèse à l'état de l'art (2026-05-27) du modèle Phi-4-Multimodal de Microsoft, candidat ASR pour Deckle via ONNX Runtime + DirectML. Identité, performance FR, écosystème C#, risques."
type: research
---

# Phi-4-Multimodal — Synthèse pour décision Deckle (état au 2026-05-27)

> Source : sub-agent recherche externe (general-purpose) lancé en session 2026-05-27, sources triangulées (model card HF, paper arXiv 2503.01743, Open ASR Leaderboard arXiv 2510.06961v4, Microsoft Q&A, blogs Azure et MS Research, GitHub issues).

## Context

Phi-4-Multimodal est candidat pour devenir le moteur ASR central de Deckle (app Windows desktop .NET 10 / WinUI 3 sur RX 7900 XT, 20 GiB VRAM). Décision en suspens : rester sur Voxtral Mini 3B (Python + ROCm, déjà étudié dans le POC `poc/voxtral-bf16`) ou basculer sur Phi-4-Multimodal (ONNX Runtime + DirectML, voie Native Windows alignée avec la stratégie d'écosystème Microsoft).

## 1. Identité du modèle

Sortie publique : `microsoft/Phi-4-multimodal-instruct` mis en ligne sur Hugging Face en **février 2025** (entraînement déc. 2024 — janv. 2025, data cutoff juin 2024). Annonce officielle Microsoft : 26 février 2025, blog Azure « Empowering innovation: The next generation of the Phi family ».

Variantes vivantes aujourd'hui (toutes Microsoft officielles) :

- `Phi-4-multimodal-instruct` (safetensors, 5,6 B paramètres) — la voie audio + vision.
- `Phi-4-multimodal-instruct-onnx` (export ONNX INT4) — collection mise à jour le **10 juillet 2025**. C'est la voie qui concerne Deckle.
- `Phi-4-mini-instruct` (3,8 B, texte seul, sans audio).
- `Phi-4-reasoning-vision-15B` (mars 2026, raisonnement vision/math, **pas d'audio**).

Variante recommandée pour la transcription FR : la *seule* qui a l'audio reste **Phi-4-multimodal-instruct**, en ONNX pour Deckle. Aucun successeur ASR n'est sorti depuis 15 mois — ambivalent (pas de remplaçant mais aucune itération non plus).

## 2. Architecture et capacités

Architecture *Mixture-of-LoRAs* : un backbone gelé **Phi-4-Mini 3,8 B** (32 couches transformer, GQA 24q/8kv, hidden 3072) + un **LoRA Vision 370 M** + un **LoRA Audio 460 M**. L'encodeur audio est *propre à Microsoft* — **3 couches conv + 24 blocs conformer**, features log-mel 80-dim, projecteur MLP 2 couches vers l'espace d'embedding texte. Ce n'est **pas** un encodeur Whisper recyclé contrairement à Voxtral. Données audio : 2 M heures d'entraînement, fine-tuning supervisé task-specific.

Langues audio supportées : **8** (en, zh, de, **fr**, it, ja, es, pt). Capacités au-delà de l'ASR : traduction de parole, **summarization audio** (premier modèle open à le faire), QA sur audio, audio understanding (musique, événements). Contexte 128k tokens, audio jusqu'à 40 s en transcription / 30 min en summarization.

Source : model card HF officielle + paper arXiv 2503.01743 (Phi-4-Mini Technical Report, mars 2025).

## 3. Performance transcription FR

Source la plus fiable et récente : *Open ASR Leaderboard* paper arXiv 2510.06961v4, **publié 30 mars 2026**, blog HF du 21 novembre 2025. Track multilingue sur CoVoST-2 et MLS.

| Modèle | WER FR | RTFx |
|---|---|---|
| ElevenLabs Scribe v2 (cloud) | 2,27 % | — |
| AssemblyAI Universal 3 Pro (cloud) | 3,74 % | — |
| Cohere Transcribe (cloud) | 4,05 % | 491 |
| **Voxtral Small 24B** | **4,13 %** | 42,0 |
| NVIDIA Canary 1B v2 | 4,83 % | 634 |
| Speechmatics Enhanced | 5,04 % | — |
| **Phi-4 Multimodal Instruct** | **5,20 %** | **78,2** |
| Meta Omnilingual ASR LLM 7B v2 | 5,34 % | 21,2 |
| Whisper Large v3 | 6,36 % | 111 |

Lecture : **Phi-4 bat Whisper-v3 sur le FR** (5,20 contre 6,36) avec un RTFx ~25 % moins bon mais largement temps-réel. **Voxtral Small 24B est devant Phi-4** (4,13) mais à 24 B donc hors-jeu pour une RX 7900 XT. **Voxtral Mini 3B n'apparaît pas dans le leaderboard 2026** — les chiffres du paper Voxtral (4,87 sur MCV-fr) restent à valider sur la méthodologie standardisée du leaderboard. Le seul chiffre indépendant FR retrouvé pour Phi-4 (étude Québec FR, arXiv 2508.21193) : **15,3 % WER avec le meilleur prompt** sur du FR québécois — accents marqués → dégradation forte mais ordre de grandeur cohérent avec la limitation reconnue par Microsoft sur les accents non-standards.

## 4. Écosystème ONNX et intégration Windows .NET — point critique

Officiellement documenté Microsoft :

- Export ONNX INT4 officiel `microsoft/Phi-4-multimodal-instruct-onnx`, variantes CUDA et **DirectML** explicites (gpu-int4-rtn-block-32, FP16 in/out).
- DirectML couvre **tout GPU DirectX 12** — donc RX 7900 XT natif, sans ROCm. C'est le bénéfice stratégique majeur vs Voxtral.
- Binding C# officiel `Microsoft.ML.OnnxRuntimeGenAI.DirectML` (NuGet) + sample C# **HelloPhi4MM** dans `microsoft/onnxruntime-genai/examples/csharp/`.
- Doc Microsoft Learn « Get started with Phi3 and other language models in your Windows app with ONNX Runtime Generative AI » couvre le pattern.

Trous documentés et préoccupants :

- Bug Microsoft Q&A (18 mars 2025) : payload `AudioContentItem` Azure SDK rejeté → contournement par payload brut, **non corrigé officiellement**, statut « self-resolved » par l'utilisateur.
- Limitation officielle Microsoft (Q&A 19 mars 2025, modérateur SRILAKSHMI C) : **text + audio + image dans le même prompt non supportés**. Pour Deckle (ASR pur, jamais d'image en entrée) c'est sans impact.
- Issue `onnxruntime-genai#1296` (mars 2025) : modèle ONNX qui tombe en CPU au lieu de GPU sur NVIDIA — closed sans détail de résolution. Pas d'équivalent DirectML/AMD documenté, ambigu : soit ça marche sans bug rapporté, soit personne ne l'a vraiment essayé.
- Issue `ai-dev-gallery#249` (27 février 2025, **toujours OPEN au 2026-05-27**) : pas de sample Phi-4 Multimodal dans l'AI Dev Gallery Microsoft. L'absence d'un sample « vitrine » 15 mois après la sortie est un signal faible mais réel d'investissement Microsoft.
- Build ONNX exige `onnxruntime` **nightly** + PyTorch nightly + numpy < 2.0, et les JSON de config sont *écrits à la main*. La voie ONNX n'est pas une chaîne stable type « NuGet et c'est parti ».

VRAM réelle : non documentée officiellement (variante INT4 → estimé ~3-4 Go pour le backbone + ~1 Go audio LoRA, large marge sur 20 Go de RX 7900 XT). Aucune mesure RTF DirectML AMD publiée.

## 5. Limites et risques

Modes d'échec connus (model card officielle) : dégradation forte sur accents non-standards et non-anglais (confirmé par étude Québec FR 25,1 % → 15,3 %), sensibilité au bruit de fond, *reduced performance for non-English languages* — c'est exactement le profil utilisateur Deckle.

Entraînement spontané vs dictée : 2 M h d'audio, mix de transcriptions humaines fortes/faibles + synthétique. Microsoft ne précise pas la proportion de TTS. Pas la même problématique que Voxtral où le TTS pose un problème explicite, mais pas d'affirmation forte côté spontané non plus.

Licence : **MIT**. Aucune restriction commerciale ou non-commerciale. Le poids des modèles et le code sont libres. Probablement le meilleur licensing possible pour Deckle.

Roadmap : Microsoft pousse activement la famille Phi-4 (reasoning, reasoning-plus, mini-reasoning, reasoning-vision-15B sorti mars 2026). **Aucun « Phi-5 » annoncé**. Le multimodal-instruct reste *le* modèle audio, sans successeur prévu — risque de stagnation plutôt que d'obsolescence.

## Recommandation au 2026-05-27

Phi-4-Multimodal est **techniquement prêt pour une intégration .NET production sur Windows + AMD**, *mais avec des trous qu'il faut accepter en toute conscience*.

Ce qui est solide : licence MIT, export ONNX DirectML officiel Microsoft, binding C# NuGet existant, sample HelloPhi4MM officiel, WER FR (5,20 %) qui bat Whisper-v3, RTFx 78 largement temps-réel — ce serait la première chaîne 100 % Windows-native pour Deckle (fin de ROCm/Python).

Ce qui inquiète : aucun chiffre de RTF DirectML sur AMD publié (il faut bencher), build ONNX qui exige du nightly tooling, absence du sample dans l'AI Dev Gallery 15 mois après sortie, modes d'échec accents non-standards reconnus par Microsoft, et Voxtral Small 24B reste devant en qualité brute (mais hors-budget GPU).

Décision pragmatique : Phi-4-Multimodal ONNX/DirectML est le bon pari stratégique pour Deckle si l'objectif d'émancipation Windows-native prime sur le dernier point de WER. Avant de basculer, un **POC mesuré** est non-négociable : run `HelloPhi4MM` sur la RX 7900 XT, mesurer RTF et VRAM réels, et tester un set de 20-30 enregistrements représentatifs (dictée FR spontanée, accent du sud, bruit modéré) pour valider que la dégradation accents documentée ne te touche pas. Si le POC passe, c'est un gain net vs Voxtral/Python/ROCm. Si le RTF DirectML AMD est mauvais ou si les accents te plantent, on reste sur Voxtral.

## Sources

- [microsoft/Phi-4-multimodal-instruct — HF](https://huggingface.co/microsoft/Phi-4-multimodal-instruct) (model card, MIT, février 2025)
- [microsoft/Phi-4-multimodal-instruct-onnx — HF](https://huggingface.co/microsoft/Phi-4-multimodal-instruct-onnx) (collection mise à jour 10 juillet 2025)
- [Phi-4-Mini Technical Report — arXiv 2503.01743](https://arxiv.org/pdf/2503.01743) (mars 2025)
- [Open ASR Leaderboard paper — arXiv 2510.06961v4](https://arxiv.org/html/2510.06961v4) (30 mars 2026, chiffres FR définitifs)
- [Open ASR Leaderboard blog HF](https://huggingface.co/blog/open-asr-leaderboard) (21 novembre 2025)
- [onnxruntime-genai Phi-4 multi-modal example](https://github.com/microsoft/onnxruntime-genai/blob/main/examples/python/phi-4-multi-modal.md)
- [Microsoft Q&A — audio input issue](https://learn.microsoft.com/en-us/answers/questions/2236277/phi-4-multimodal-instruct-unable-to-send-audio-inp) (18 mars 2025)
- [Microsoft Q&A — text+audio+image not supported](https://learn.microsoft.com/en-us/answers/questions/2236372/phi-4-multimodal-does-not-support-text-audio-image) (19 mars 2025)
- [onnxruntime-genai issue #1296 — GPU loading](https://github.com/microsoft/onnxruntime-genai/issues/1296) (4 mars 2025)
- [ai-dev-gallery issue #249 — Phi-4 sample request](https://github.com/microsoft/ai-dev-gallery/issues/249) (27 février 2025, toujours open)
- [Azure blog — Empowering innovation: next gen Phi](https://azure.microsoft.com/en-us/blog/empowering-innovation-the-next-generation-of-the-phi-family/) (26 février 2025)
- [Phi-4-reasoning-vision-15B — Microsoft Research](https://www.microsoft.com/en-us/research/blog/phi-4-reasoning-vision-and-the-lessons-of-training-a-multimodal-reasoning-model/) (mars 2026, hors audio)
- [Benchmarking Québec French ASR — arXiv 2508.21193](https://arxiv.org/html/2508.21193) (chiffre 15,3 % FR québécois)
- [Voxtral paper — arXiv 2507.13264v1](https://arxiv.org/html/2507.13264v1) (référence pour comparaison Mistral)
