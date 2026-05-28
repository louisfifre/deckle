---
name: research-asr-native-windows-amd-routes-2026-05-28
description: "Cartographie des voies réalistes pour exécuter un modèle ASR multimodal (Phi-4, Voxtral, Gemma4, Whisper) en natif Windows .NET + AMD RX 7900 XT au 2026-05-28. Diagnostic exact du bug Phi-4 OGA audio (cause-racine LoRA non activé, patch local de quelques dizaines de lignes), découverte d'un export ONNX communautaire de Voxtral Mini 3B 2507, runtime Rust Burn/wgpu Voxtral 4B Realtime sur Vulkan natif Windows, cadrage Gemma4 OGA, voie de repli Whisper dynamic windowing + VAD énergie + distil-fr. Issu de cinq sub-agents lancés en session 2026-05-27/28."
type: research
date: 2026-05-28
---

> Source : cinq sub-agents recherche (Claude general-purpose) lancés en session 2026-05-27/28, retours intégrés et curaturés par la session principale. Voir aussi l'entrée du 2026-05-27 (suite 6) dans [`benchmark/JOURNAL.md`](../../benchmark/JOURNAL.md) pour l'amont expérimental (test verbatim du sample Microsoft PhiCookBook). Aucune décision tranchée à ce stade — cette fiche cartographie ce qui est *possible*, ses coûts respectifs, ses risques.

# Context

La fin de session 2026-05-27 avait exprimé une direction stratégique long terme pour l'épine dorsale modèles de Deckle : *Native Windows via ONNX Runtime + DirectML* — un chemin qui supprimerait la dépendance Python + ROCm pour tous les modèles embarqués. Phi-4-Multimodal-Instruct de Microsoft était le premier candidat ASR identifié dans la fiche [`research--phi-4-multimodal-state-of-the-art--2026-05-27.md`](research--phi-4-multimodal-state-of-the-art--2026-05-27.md).

Le POC monté dans la foulée (worktree `phi4-onnx-poc`, projet C# `benchmark/cs/PhiBench/`) a révélé au palier 1 que toutes les sorties audio Phi-4 via `onnxruntime-genai-directml` sont des refus type *« I'm sorry, but I can't transcribe directly the audio you mentioned »*. Le test verbatim du sample officiel Microsoft `PhiCookBook/LabsPhi4-MultiModal-02Audio` a confirmé que ce bug existe chez Microsoft eux-mêmes, dans toutes les configurations testées (DirectML 0.13.0, DirectML 0.7.0-rc2, CPU 0.13.0, CPU 0.7.0-rc2, quatre variantes de prompt).

Une première synthèse rapide concluait *« no-go en l'état »* — verdict que Louis a rejeté avec une feedback méta importante : *« t'as l'air de dire que c'est fichu, mais tu cherches pas beaucoup quoi. (...) on est là pour explorer »*. La phase d'exploration qui suit cinq agents en parallèle a effectivement renversé la lecture : non seulement le bug Phi-4 OGA est diagnostiquable à la ligne près et patchable en quelques dizaines de lignes, mais d'autres voies natives Windows AMD existent qui n'avaient pas été repérées (export ONNX communautaire de Voxtral 3B 2507, runtime Rust Burn/wgpu sur Vulkan).

Cette fiche consigne ce qui est ressorti, pour ne pas reperdre la matière au prochain creux de contexte.

# Voie 1 — Patcher OGA pour Phi-4 audio (LoRA speech non activé)

## Cause-racine localisée

Le bug ne vit ni dans le splice des embeddings audio, ni dans l'expansion du token `<|audio_1|>`, ni dans l'`audio_projection_mode`, ni dans l'ordre des opérations du pipeline. L'hypothèse initiale de `greeeenz` sur l'issue #1455 (*« OGA embeds the audio speech part of the prompt compared to transformers/torch, which does it after »*) est factuellement réfutée par la lecture du code des deux côtés : les deux implémentations splicent l'audio dans le tensor d'embeddings textuels exactement au même endroit dans le pipeline.

Le vrai problème : **le LoRA speech n'est jamais activé**. Phi-4-multimodal a un base decoder (Phi-4 instruct classique) plus deux LoRAs (un vision rank 320 alpha 640, un speech rank 320 alpha 640) qui réécrivent chaque attention QKV/O et chaque MLP gate/up/down du décodeur. Le LoRA speech est ce qui apprend au décodeur à consommer des embeddings audio et produire une transcription au lieu d'un refus de safety. Sans LoRA actif, le décodeur reçoit les embeddings audio comme des vecteurs out-of-distribution et défaute sur son comportement le plus sûr — *« I can't transcribe directly »*.

L'activation du LoRA est tentée dans `src/models/multi_modal.cpp:705-727`, le constructeur de `MultiModalPipelineState` :

```cpp
} else if (speech_state_ != nullptr && model_.config_->model.speech.adapter_filename.has_value()
    && num_audio_tokens_ > 0) {                  // <-- TOUJOURS FAUX ICI
  ... LoadAdapter(...); decoder_state_->SetActiveAdapter(...);
}
```

Le membre `num_audio_tokens_` est déclaré dans `multi_modal.h:175-176` avec un initializer `{}`, donc vaut zéro à la construction. Il n'est rempli que plus tard, dans `MultiModalPipelineState::SetExtraInputs` (lignes 729-740), qui s'exécute *après* le constructeur. Le test du `else if` est donc structurellement faux au moment où il est évalué, le LoRA n'est jamais chargé, le décodeur tourne sans adapter et refuse.

Le `git blame` confirme : le bug speech existe depuis le commit `6b99e0d` *« Whisper Redesigned Solution (#1229) »* du 1er juillet 2025. Le bug vision symétrique (issue #1383, image qui devient *« highly distorted or corrupted »*) a la même cause-racine et existe depuis le commit Phi-4-mm originel `59560433` du 27 février 2025. **Un seul fix corrige les deux symptômes.**

## Le patch

Mécaniquement minuscule. Déplacer l'activation du LoRA du constructeur vers `SetExtraInputs`, après que les compteurs `num_image_tokens_` / `num_audio_tokens_` aient été calculés. L'infrastructure pour activer un adapter à ce moment existe déjà (`State::SetActiveAdapter` est callable à n'importe quel moment avant `Run()`). Quelques dizaines de lignes dans un seul fichier (`src/models/multi_modal.cpp`), pas de changement d'ABI, pas de changement de binding C# ou Python, pas de nouvelle API publique. Pure réorganisation du control-flow.

## Risques résiduels à vérifier après patch

1. **Persistance du LoRA pendant la génération autoregressive.** Le LoRA doit rester actif pour chaque token généré, pas uniquement le premier. À confirmer en lisant l'impl de `State::SetActiveAdapter` et l'usage des `OrtRunOptions` dans `State::Run`. Si le LoRA ne persiste pas, le premier token est correct mais les suivants dégradent.
2. **Comportement du graphe embedding.** Le splice doit fonctionner correctement. PR #1701 (mergée 2025-08-26) l'a déjà partiellement corrigé côté allocation de `audio_features`. À confirmer par un debug dump.
3. **Tokenization du special audio token.** OGA réécrit `<|audio_1|>` en `<|endoftext11|>` en interne (`phi_multimodal_processor.cpp:54-55`) en supposant que ce dernier mappe à l'ID 200011. Si le tokenizer du bundle ONNX ne respecte pas ce mapping, les positions audio dans `input_ids` sont fausses et le scatter d'embeddings splice à la mauvaise position.
4. **Mapping de `audio_projection_mode`**. OGA hardcode 0/1/2/3 ; transformers utilise des strings (`"speech"` vs `"vision"`). La cohérence est à confirmer par un debug dump sur les inputs de `speech.onnx`.

## Stratégie de vérification

Trois couches d'évidence avant de déclarer victoire :

1. **LoRA effectivement chargé.** `std::cerr << "speech adapter loaded\n"` à côté de `LoadAdapter`. La ligne doit apparaître exactement une fois par prompt.
2. **LoRA effectivement appliqué au décodeur.** Debug dump des `OrtRunOptions` au premier `decoder.Run()` ; l'active adapter doit y figurer.
3. **Test de régression contre transformers.** Cinq audios courts (~5-10 s anglais). Run via transformers/torch avec `model.load_adapter("speech-lora") + model.set_adapter("speech")` ET via OGA patché. Les deux transcriptions doivent matcher à la ponctuation près. Si transformers transcrit et OGA refuse encore, le fix n'a pas atteint le décodeur — recheck risque 1. Si les deux refusent, le tokenizer ou le prompt template est en cause — recheck risque 3.

## Question performance soulevée par Louis

Le LoRA speech fait ~460 M paramètres. Une fois chargé via `Adapters::LoadAdapter`, il reste en VRAM en permanence — pas de détour vers la RAM système à chaque token. À chaque forward pass, ORT applique le delta LoRA en VRAM avec un matmul supplémentaire par couche (`output = W_base @ x + α × (A @ B) @ x` où A et B sont les matrices low-rank). Coût permanent estimé à 10-15 % de latence supplémentaire par token, prédictible et constant. Pas un cache-miss à chaque step. Viable, à mesurer en RTF réel après patch.

# Voie 2 — Voxtral Mini 3B 2507 via ONNX Runtime DirectML

## La trouvaille majeure de la session

L'affirmation de la fiche `research--phi-4-multimodal-state-of-the-art--2026-05-27.md` *« Voxtral n'a pas d'export ONNX officiel — devient un cul-de-sac de production .NET »* est obsolète au sens strict. **Il existe un export ONNX communautaire officiel** : [`onnx-community/Voxtral-Mini-3B-2507-ONNX`](https://huggingface.co/onnx-community/Voxtral-Mini-3B-2507-ONNX), publié par Xenova (HF staff) en juillet 2025. 36.5 GB total, contient `config.json`, `generation_config.json`, `preprocessor_config.json`, `chat_template.jinja`, `tokenizer.json` (12.6 MB), `tekken.json` (14.9 MB), `special_tokens_map.json`, et un dossier `onnx/` avec audio encoder, decoder merged, embed_tokens en variantes `fp32 / fp16 / q8 / q4 / q4f16`.

**C'est exactement le Voxtral Mini 3B 2507** — le modèle validé qualité par Deckle dans `voxtral-transformers-validation-0006` (WER médian 0.30 sur le corpus voxtral-val-30, post-fix max_new_tokens). Pas le 4B Realtime, pas un dérivé. Un bug initial (token `[AUDIO]` absent remplacé par `<SPECIAL_24>`, mmproj absent, NaN logits) a été résolu par Xenova le jour même de la publication via la discussion #1.

## Pipeline d'inférence en C#

ONNX Runtime DirectML (`Microsoft.ML.OnnxRuntime.DirectML`) charge sans problème les `.onnx` du bundle. Pas besoin de passer par la couche GenAI cassée pour Phi-4 — Voxtral peut tourner sur ORT direct, plus mature, sans le bug LoRA. Le pipeline d'inférence à porter côté C# se décompose en quatre morceaux :

1. **Prétraitement mel spectrogramme.** Voxtral attend du 16 kHz mono, log-mel 80 bins, fenêtres de 25 ms, hop 10 ms. Implémentable en C# pur avec NAudio pour la lecture WAV + une FFT type System.Numerics ou un port direct du mel filterbank de whisper.cpp. Quelques jours.
2. **Audio encoder.** `audio_encoder.onnx` (Whisper-style Conformer 640M) prend les features mel et produit des embeddings audio. Un `InferenceSession` ORT.DirectML.
3. **Embed tokens + chat template.** Construction du prompt avec injection du token `[TRANSCRIBE]` (que `mistral-common` injecte implicitement côté Python). À reproduire à la main en C# : lire `chat_template.jinja`, appliquer aux messages, splicer les audio embeddings à la position des audio tokens dans le tensor d'embeddings.
4. **Decoder génératif avec KV cache.** Boucle autoregressive sur `decoder_model_merged.onnx`. Peut s'appuyer sur le pattern Phi-3-mini ORT GenAI déjà documenté publiquement (cf. nietras.com guide *« Phi-3-mini in 30 lines of C# with ONNX Runtime GenAI »*), OU implémenter manuellement la gestion KV cache via ORT.

## Effort estimé

2 à 4 semaines de travail concentré pour un PoC fonctionnel transcrivant un audio FR en C# sur RX 7900 XT. C'est le chemin le plus court vers du *natif Deckle, sans Python, avec qualité préservée* (le modèle est déjà validé). L'agent recherche le note comme la voie « la plus alignée avec une stratégie de production ».

## Risques

1. **Qualité française des quantizations.** Le modèle a été validé en BF16 transformers — pas en Q4 ou Q4F16 ONNX. L'hypothèse Cohere ([arXiv 2407.03211](https://arxiv.org/abs/2407.03211), -16.6 % perception humaine FR au passage FP16 → 4-bit) suggère que Q4 dégrade significativement le français. **Mesurer FP16 et Q4F16 sur `voxtral-val-30` avant de capitaliser** est la première chose à faire — c'est le palier de cadrage qui dit *go ou recadrer*. FP16 fait ~9 GB encoder + decoder + embed, tient large dans 20 GB VRAM.
2. **Désalignement de stack vs whisper.cpp/Vulkan déjà dans Deckle.** On introduit un second runtime ONNX/DirectML cohabitant avec `ggml-vulkan.dll`. Doctrine Deckle à arbitrer le moment venu — pas un blocker immédiat.
3. **DirectML en sustained engineering.** Microsoft pousse Windows ML pour les nouveaux projets, mais DirectML reste maintenu et fonctionnel. Risque faible mais à surveiller.

# Voie 3 — Voxtral Mini 4B Realtime via TrevorS Burn/wgpu/Vulkan

## Le runtime qui existe déjà

[`TrevorS/voxtral-mini-realtime-rs`](https://github.com/TrevorS/voxtral-mini-realtime-rs), v0.2.5 sortie le 2 avril 2026, 55 commits, 4 releases, projet actif. Implémentation pure Rust de Voxtral Mini 4B Realtime ASR + Voxtral 4B TTS via framework **Burn** (équivalent Rust de PyTorch côté abstraction tenseurs) + kernels **CubeCL**. Backends : **WGPU natif (Vulkan sur Windows/Linux, Metal sur macOS) + WGPU dans le browser via WASM**. Bench publié : NVIDIA DGX Spark, RTF 0.416 sur 16 s d'audio, 19.4 tok/s en Q4 GGUF natif.

C'est *exactement* la promesse architecturale recherchée : **GPU AMD sur Windows via Vulkan, sans Python, sans CUDA**. Kernels Q4 WGSL custom (tiled pour M ≤ 4 single-token decode, naive pour M > 4). Cohérent avec la doctrine Deckle (ADR-0008 : *Rester sur Vulkan pour les backends GPU natifs*).

## La question bloquante — 4B Realtime ≠ 3B 2507

Le runtime cible **Voxtral Mini 4B Realtime 2602**, pas le 3B 2507 que Deckle a validé en qualité. Le 4B Realtime a déjà été testé par Louis avant le POC formel (via les PRs llama.cpp #19698 et #20625) et écarté parce que *« le mode streaming ne donnait pas de qualité satisfaisante »* — verdict de session 2026-05-23 inscrit dans la note *« État cumulé »* de [`benchmark/JOURNAL.md`](../../benchmark/JOURNAL.md). Réintroduire le 4B Realtime via TrevorS pose la question : *le verdict qualité 4B Realtime était-il dû au modèle ou au backend ?* Si c'était le backend (PR llama.cpp streaming pas mûre), TrevorS pourrait passer. À mesurer avant de capitaliser.

## Effort estimé

2-3 jours pour cloner, builder Windows + Vulkan, mesurer RTF sur 7900 XT et qualité française sur voxtral-val-30. Si le bench passe : 1-2 semaines pour ajouter un crate `voxtral-ffi` qui expose une API C stable (load model, transcribe wav, free), puis quelques jours pour intégrer en P/Invoke dans un nouveau module `Deckle.Transcription.Voxtral` (pattern parent/enfant `IAsrBackend` documenté par [ADR-0010](../adr/0010-backend-asr-pluggable-via-iasrbackend.md)).

## Risques

1. **Fidélité française 4B Realtime vs 3B 2507.** Question critique. La grille de mesure existante (`voxtral-val-30` jugée par Gemini multimodal) répond apple-to-apple.
2. **Stack jeune.** Burn + CubeCL + WGSL + WGPU Vulkan est une pile expérimentale empilée. Risque de régressions WGSL spécifiques aux drivers AMD RDNA3.
3. **Bus factor 1.** TrevorS solo selon le repo. Peu de contributeurs externes documentés.
4. **Audio normalization piège.** `peak_normalize(0.95)`, audio < 0.02 produit silence. À reproduire fidèlement côté hôte .NET.

# Voie écartée — Gemma4 multimodal OGA/DirectML

PR [#2103](https://github.com/microsoft/onnxruntime-genai/pull/2103) *« Add Gemma4 multimodal support (vision + audio) »* mergée 2026-05-04 par `apsonawane` (Microsoft), reviewée par `justinchuby`, NuGet `Microsoft.ML.OnnxRuntimeGenAI.DirectML 0.14.0` publié 2026-05-26 avec le code. Test documenté par l'auteur : *« Windows SAPI TTS → model correctly identifies speech content »*. Engagement Microsoft qualitativement bien meilleur que Phi-4 (Microsoft Foundry intégration, AMD Day-0 article, samples).

**Bloqueur dur** : aucun export ONNX OGA-compatible n'est publié au 2026-05-28. Le seul export sur HuggingFace est [`onnx-community/gemma-4-E2B-it-ONNX`](https://huggingface.co/onnx-community/gemma-4-E2B-it-ONNX) au format Transformers.js (`decoder_model_merged.onnx` + `embed_tokens.onnx`, pas de `speech.onnx`, pas de `genai_config.json`). La pipeline d'export Microsoft (`mobius`) est interne. POC ne peut pas démarrer tant qu'un export OGA-compatible n'apparaît pas.

**Bloqueur structurel sur l'usage Deckle** : benchmark publié par [James Ding](https://twango.dev/writing/gemma4-asr-benchmark) sur Gemma4 E2B ASR (8 datasets short-form anglais, RTX 6000 Blackwell + vLLM bf16) : **2 203 % WER sur clips sub-1 seconde**, catastrophic failure under 3 s, hallucinations en bruit. La fenêtre sub-3 s est *exactement* la fenêtre typique de dictée hotkey Deckle. Auteur écrit : *« Don't swap it in for a dedicated ASR model. »*

**Alternative émergente** : [Daniel Demin a shippé Gemma4 ASR en .NET 10 desktop](https://dev.to/mdemin729/shipping-gemma-4-speech-recognition-in-a-windows-net-desktop-app-a-5-variant-model-selection-tour-2l8i) ([parlotype](https://github.com/mdemin729/parlotype), 25 mai 2026) via **llama.cpp + Vulkan** parce que *« onnxruntime-genai does not support Gemma 4's architecture yet »*. Cohérent ADR-0008 Deckle. Default ship : E4B-it-Q4_K_M GGUF (~5.9 GB), 13.82 % WER LibriSpeech test-other anglais, RTF 0.038 CUDA. À garder en référence si Gemma4 redevient candidat.

# Voie de repli — Whisper.cpp dynamic windowing + VAD énergie + distil-fr

Cette voie n'est pas une alternative ASR — c'est une refonte de la stack actuelle pour résoudre la latence interactive de la dictée. À engager si les voies 1-3 n'aboutissent pas, OU en parallèle additif si le timing le permet.

## Trois trouvailles

**Un — `audio_ctx` est déjà déclaré dans la struct P/Invoke** ([`WhisperStructs.cs:49`](../../src/Deckle.Transcription.Whisper/Pinvoke/WhisperStructs.cs)) mais jamais branché par `WhisperParamsMapper.Apply`. Le branchement = ~20 lignes : un champ POCO sur `EngineSettings`, un `if` dans le mapper, un helper `ComputeAudioCtx(int sampleCount, int sampleRate)` qui applique la formule canonique `audio_ctx = round_to_64((audio_seconds / 30) * 1500 + 128)` clampée à `[768, 1536]` par la guidance ggerganov (discussion [#297](https://github.com/ggml-org/whisper.cpp/discussions/297)). Issue [#1855](https://github.com/ggml-org/whisper.cpp/issues/1855) mesure sur Common Voice : `base.en` WER 20.06 → 19.2 avec formule adaptative, 3-3.5× speedup CPU.

**Deux — `vad_simple` de whisper.cpp est trivial.** Le VAD que `whisper-stream` utilise depuis 2023 fait ~25 lignes de C++ : moyenne absolue d'amplitude (`fabsf`, pas RMS pour économiser le `sqrt`) + filtre passe-haut RC à 1 pôle (cutoff 100 Hz, rejet ronflement secteur et souffle). Le test `energy_last > vadThreshold * energy_all` retourne `false` quand le dernier 1 s est plus calme que la moyenne — détecte le silence après parole, le trigger exact de *« l'utilisateur a fini sa phrase »*. Port C# direct ~25 lignes. 3-4 ordres de magnitude plus rapide que Silero-VAD. Le défaut signature de Silero (latence intolérable en interactif) disparaît avec cette approche énergétique pure.

**Trois — Distil-Whisper FR existe en GGML drop-in.** [`bofenghuang/whisper-large-v3-distil-fr-v0.2`](https://huggingface.co/bofenghuang/whisper-large-v3-distil-fr-v0.2). 2 decoder layers (vs 32 dans large-v3), même encoder, 49 % des paramètres, 5.8× plus rapide à WER similaire selon l'auteur (*« within 2% »*). Entraîné sur ~10 400 h de FR. Format `ggml-model-q5_0.bin` accessible directement. **Drop-in pour le `libwhisper.dll` existant** — pointer `EngineSettings.Model` dessus et le backend charge transparent. À ajouter à `SpeechModels` catalog dans `Deckle.Transcription.Whisper.Setup`.

## Plan en 5 phases — 5-6 jours total

| Phase | Effort | Contenu | Ship indépendant ? |
|---|---|---|---|
| 1 — Foundation | 1 j | `audio_ctx` wired + CPU fallback + warmup probe + distil-fr au catalog | Oui, additif |
| 2 — VAD énergie | 1 j | `EnergyVad.cs` port C# (~50 LoC) + settings + tests unitaires | Oui, autonome |
| 3 — Window segmenter | 3 j | `WindowSegmenter.cs` + refactor pipeline per-segment | Le gros morceau |
| 4 — Tuning | 1 j | Defaults : `EntropyThreshold 2.6`, `UseContext = false` dynamique. Bench voxtral-val-30 en 4 runs | Oui, validation |
| 5 — Polish | 1 j (optionnel) | HUD live update via `NewSegment` event existant | Polish |

## Risque ship-blocker

Stabilité Vulkan sur RX 7900 XT. Issue [#2867](https://github.com/ggml-org/whisper.cpp/issues/2867) est fermée mais sans résolution explicite ; passer en plus petites passes GPU augmente l'exposition au bug. **Mitigation obligatoire dès la phase 1** : CPU fallback wired + warmup probe de 1 s à la fin de `LoadModelAsync`. Si la session GPU initialise mais que la première inférence crashe (signature #2867), bascule auto en CPU avec log Verbose. C'est la sécurité dont la maison manquait jusqu'ici.

## Idée connexe — DSP à l'enregistrement

Émergée en cours de session. Si on peut faire du DSP en C# pur (high-pass filter à 100 Hz, mean-absolute), on peut empiler d'autres traitements en amont de la transcription pour compenser un micro à faible gain et nettoyer l'entrée : normalisation de gain (peak ou RMS), low-pass à 8 kHz (la voix intelligible monte rarement plus haut), noise gate sur RMS, retrait DC offset, optionnellement compression dynamique. Chaque filtre quelques dizaines de lignes. À ranger comme module audio en amont, **indépendant du backend ASR choisi** — s'applique avant que Phi-4 / Voxtral / Whisper voient l'audio. À creuser après qu'un backend soit choisi et stabilisé.

# Verdict empirique — sample Microsoft PhiCookBook

Test verbatim du sample officiel `microsoft/PhiCookBook/md/04.HOL/dotnet/src/LabsPhi4-MultiModal-02Audio` (publié par Microsoft, pinned `Microsoft.ML.OnnxRuntimeGenAI.DirectML 0.7.0-rc1`). Reproduit dans `D:\tmp\PhiCookBookTest` avec audio `dcad692a` du corpus voxtral-val-30 (1.7 s « Et toujours douter un peu. »). Quatre configurations testées :

| Config | NuGet | EP | Résultat |
|---|---|---|---|
| PhiBench Deckle | 0.13.0 DirectML | dml | Refus *« can't transcribe directly »* |
| PhiBench Deckle | 0.13.0 DirectML | cpu | Refus *« can't help with that »* |
| Sample MS verbatim | 0.7.0-rc2 DirectML | dml | **Crash MatMulNBits LoRA Q4** (incompat format ONNX récent) |
| Sample MS verbatim | 0.7.0-rc2 DirectML | cpu (config Debug standard) | **Refus identique** *« can't perform tasks like transcribing audio content »* (32.9 s) |

Conclusion empirique forte : **la chaîne C# Microsoft pour Phi-4 audio est cassée chez Microsoft eux-mêmes, sur leur propre modèle, dans leur propre sample, depuis la première version publiée mars 2025**. Le sample MS pin une version archaïque (0.7.0-rc1) qui crash sur le modèle ONNX actuel (publié juillet 2025) parce que le format INT4 du LoRA adapter a évolué. En CPU mode (config Debug standard), il atteint la génération mais refuse comme tous les autres. Le bug n'est ni notre code, ni notre prompt, ni notre EP. Confirmation indépendante par l'agent A (diff transformers vs OGA) : la cause-racine est l'activation du LoRA jamais déclenchée parce que conditionnée à un compteur structurellement zéro.

# POC palier 2 — patch local appliqué et testé (2026-05-28)

Suite à la fiche ci-dessus, **le patch local OGA a été produit et testé end-to-end sur la machine cible**. Cette section consigne les observations brutes, sans verdict — la matière sert à décider d'une suite et à alimenter éventuellement un retour upstream.

## Patch produit

Branche `fix/phi4-lora-activation-after-extra-inputs` sur le tag `v0.13.0` (commit upstream `2d30e49ff403`) du clone local `D:\workspace\onnxruntime-genai\`. Diff de 33 lignes (+24 / -9) sur un seul fichier `src/models/multi_modal.cpp` :

- Le bloc IF/ELSE IF d'activation LoRA est retiré du constructeur `MultiModalPipelineState` (où `num_image_tokens_` et `num_audio_tokens_` valent zéro par initialisation `{}`)
- Le bloc est réinjecté à la toute fin de `MultiModalPipelineState::SetExtraInputs`, *après* la population des compteurs par `GetNumImageTokens` / `GetNumAudioTokens`
- Deux lignes `std::cerr << "[deckle-phi4-poc] {vision|speech} LoRA adapter loaded (num_*_tokens=N)\n"` ajoutées à côté de chaque `LoadAdapter` pour la vérification couche 1
- Aucun changement de header, aucun changement de signature, aucun nouveau membre — la P/Invoke surface reste identique

Diff archivé sous [`docs/research/phi4-oga-lora-activation--2026-05-28.patch`](phi4-oga-lora-activation--2026-05-28.patch). Commit local `e7d26fc` sur le clone hors-repo.

## 4 risques résiduels — résolus par lecture amont

Avant le test, lecture confirmation des quatre risques posés dans la section *Voie 1 — Risques résiduels* de cette fiche :

1. **Persistance du LoRA pendant la génération autoregressive** — `State::SetActiveAdapter` (lignes 234-244 de `src/models/model.cpp` à `v0.13.0`) ajoute l'adapter à `run_options_->AddActiveLoraAdapter(...)`. Le `run_options_` est un membre persistant de la base class `State`, retransmis à chaque `decoder.Run()`. **Le LoRA reste actif pour tous les tokens autoregressifs**, pas seulement le premier. Risque levé.
2. **Comportement du graphe embedding (splice audio_features)** — PR upstream [#1701](https://github.com/microsoft/onnxruntime-genai/pull/1701) mergée 2025-08-26 traite déjà l'allocation `audio_features`. Pas de signal de régression observé. Risque levé.
3. **Tokenization `<|audio_1|>` → `<|endoftext11|>` ID 200011** — confirmé empiriquement dans `tokenizer.json` du bundle `gpu/gpu-int4-rtn-block-32` : entrée `added_tokens` ligne 24-27 (`id=200011`, `content="<|endoftext11|>"`) et entrée `vocab` ligne 200181. La regex sub OGA produit le bon `input_id`. Risque levé.
4. **Mapping `audio_projection_mode`** — `phi_multimodal_processor.cpp` lignes 41-50 mappe `2 = Speech, language` exactement quand `num_audios > 0 && num_images == 0` — cohérent avec transformers (qui passe la même value 2 à `audio_projection.onnx` même si l'API utilise des strings). Risque levé.

Aucun des quatre risques résiduels n'est un blocage actif. La chaîne logique du fix est saine.

## Build local de `onnxruntime-genai` patché

Réussi après itérations sur la toolchain Windows AMD. Synthèse archivée sous [`docs/reference/reference--build-onnxruntime-genai-amd-windows--1.0.md`](../reference/reference--build-onnxruntime-genai-amd-windows--1.0.md). Trois écueils notables, tous contournés sans toucher au code amont :

- **`Enter-VsDevShell` sur VS 2026** ne setup ni le PATH MSVC bin ni `LIB` correctement (bug observé, à signaler). Le wrapper `build-deckle.ps1` compense manuellement.
- **MSVC 14.51 émet le nouveau warning C4875** sur le pattern `[[gsl::suppress(int_literal)]]` de la lib GSL bundlée. OGA compile avec `/WX`, le warning devient fatal. Contourné via `$env:CL = '/wd4875'`.
- **MSVC 14.51 érige `<experimental/coroutine>` en `STL1011` static_assert**. Contourné via `/D_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS` dans la même var d'env.

DLL produit : `D:\workspace\onnxruntime-genai\build\Windows\Release\onnxruntime-genai.dll`, 2330 KB (vs 5922 KB pour le NuGet stock — différence de build flags vraisemblable, à vérifier). Dépendances ORT 1.25.0-dev téléchargées et stagées à côté. Drop-in opéré dans `benchmark/cs/PhiBench/bin/Debug/net10.0-windows/win-x64/`, anciens DLL conservés en `.stock-0.13.0-backup`.

## Vérification couche 1 — LoRA effectivement chargé

`PhiBench single` sur l'audio diagnostique `dcad692a` (1.7 s « Et toujours douter un peu. »), execution provider CPU, modèle `gpu/gpu-int4-rtn-block-32`. Stderr capturé verbatim :

```
=== PhiBench single ===
  model_path : D:\models\llm\phi4-multimodal-onnx\gpu\gpu-int4-rtn-block-32
  ep         : cpu
  loading model...
  model ready
  transcribing...
[deckle-phi4-poc] speech LoRA adapter loaded (num_audio_tokens=18)
```

**La ligne diagnostique apparaît, une seule fois, avec `num_audio_tokens=18` cohérent avec 1.4 s d'audio actif (mesure WAV header : 1.408 s).** Le constructeur n'active plus le LoRA. `SetExtraInputs` l'active après population des compteurs. Le contrôle de flow du patch fonctionne tel que prévu.

## Bug différent observé à l'application du LoRA — shape mismatch

Immédiatement après le chargement du LoRA, la première inférence plante :

```
[E:onnxruntime] Non-zero status code returned while running MatMulNBits node.
  Name:'/model/layers.0/attn/v_proj/lora_A/MatMul_Q4'
  Status Message: Input 'quantized_weight' is expected to have shape {256,96,16}, got {320,96,16}
```

Décodé :

- Le node `lora_A` au `v_proj` de la couche 0 attend un `quantized_weight` de shape `{256, 96, 16}`
- Le LoRA file fournit `{320, 96, 16}`
- L'axe qui diffère est la dimension 0 (256 vs 320). En INT4 MatMulNBits avec `block_size=32`, le shape est `[N, K/block_size, block_size/2]`. Avec K=3072 (le `hidden_size` du décodeur) et block_size=32, on a bien `K/block_size = 96` et `block_size/2 = 16` cohérents. La dimension 0 (N) correspond au output dim du LoRA A matrix, qui est le **rang du LoRA**.
- Le base decoder `phi-4-mm-text.onnx` semble avoir des nodes câblés pour **rank 256**
- Le LoRA adapter `phi-4-mm-speech.onnx_adapter` semble shipper des weights pour **rank 320**

C'est exactement le crash *« MatMulNBits LoRA Q4 incompat format ONNX récent »* déjà observé dans la table verdict empirique ci-dessus sur le sample MS verbatim. Ce qui change avec notre patch : avant, l'erreur ne se manifestait jamais parce que le LoRA n'était jamais chargé. Maintenant qu'il l'est, le mismatch shape émerge à l'exécution.

## Cause du mismatch — incertaine

**Ce qu'on observe** : deux shapes incompatibles dans le bundle officiel Microsoft `microsoft/Phi-4-multimodal-instruct-onnx`, variante `gpu/gpu-int4-rtn-block-32`, telechargé en local en mai 2026. Le base decoder attend rank 256, le LoRA file fournit rank 320.

**Ce qu'on ne sait pas encore** :

- Pourquoi cette divergence existe-t-elle ? Hypothèses non vérifiées : (a) deux versions du modèle source ont été quantizées et publiées dans le même bundle par erreur ; (b) une transformation runtime devait projeter 320 → 256 mais n'a pas été déclenchée ; (c) un téléchargement partiel ou un fichier corrompu côté local ; (d) la variante `gpu-int4-rtn-block-32` consomme un LoRA différent que la variante CPU et nos chemins se croisent ; (e) le base decoder a été exporté pour un Phi-4-Mini de rank 256 alors que le LoRA speech est entraîné sur un Phi-4-Mini légèrement différent (rank 320) — la docstring originale de la model card mentionne *« LoRA Audio 460 M »* et rank 320 / alpha 640.
- Est-ce que d'autres variantes du bundle (`cpu-int4-rtn-block-32`, `gpu-fp16`) présentent le même mismatch ? Non testé.
- Est-ce que la résolution se trouve côté tooling de génération du bundle (Microsoft `mobius`) ou côté runtime OGA (qui pourrait théoriquement projeter le LoRA en runtime) ?

Avant tout verdict, **investigation à mener** :

- Croiser avec les autres variantes du bundle officiel sur HuggingFace (regarder les shapes de leurs `phi-4-mm-speech.onnx_adapter` respectifs).
- Inspecter `phi-4-mm-text.onnx` pour confirmer le rank attendu sur tous les `lora_A` / `lora_B` nodes.
- Inspecter le `phi-4-mm-speech.onnx_adapter` (298 MB) pour confirmer la shape de toutes ses entrées.
- Vérifier les issues GitHub adjacentes : il existe peut-être déjà un report sur le mismatch shape Phi-4-mm OGA, distinct de #1455.
- Considérer une soumission upstream du patch LoRA activation : il résout proprement #1455 (24 lignes), et le mismatch shape devient alors visible et exploitable comme bug séparé.

## Statut de la voie 1 au 2026-05-28 fin de session

**Pas de verdict.** Le patch a fait ce qu'il était censé faire (LoRA chargé au bon moment, persistance du run_options garantie, control-flow validé bout en bout). Un nouveau bloqueur a émergé en aval, dont la cause-racine n'est pas encore tranchée et dont les voies de résolution n'ont pas été explorées. L'investigation reste ouverte.

Ce qui est acquis : le patch local OGA est **propre, testé empiriquement, et candidat à une PR upstream** pour résoudre #1455 indépendamment du sort du bundle officiel — il bénéficierait aussi à toute personne qui exporte son propre bundle Phi-4-mm avec rank cohérent. Le diagnostic du shape mismatch lui-même est un signal utile qu'on n'avait avant la patch.

# Voies fermées, documentées pour ne pas reperdre

- **llama.cpp mainline Voxtral.** PRs [#19698](https://github.com/ggml-org/llama.cpp/pull/19698) et [#20638](https://github.com/ggml-org/llama.cpp/pull/20638) fermées par ngxson le 23 mars 2026 (*« many model-specific code paths, considered as anti-pattern in libmtmd design »*). Issue de planification [#20914](https://github.com/ggml-org/llama.cpp/issues/20914) ouverte le même jour, stale depuis. Aucun fork actif identifié. À surveiller passivement.
- **whisper.cpp Voxtral.** Issue [#3326](https://github.com/ggml-org/whisper.cpp/issues/3326) ouverte le 15 juillet 2025, toujours open, aucun commentaire, aucun PR. Mort silencieuse.
- **antirez/voxtral.c.** Repo actif, Metal-only GPU, aucun build Windows, cible 4B Realtime. *Hard* à porter Windows + AMD : 2-4 semaines pour PoC CPU Windows, plusieurs mois pour exploiter le GPU AMD via Vulkan. À garder en référence intellectuelle mais pas une voie de production court terme.
- **mistral.rs**. v0.8.0 supporte Voxtral 4B Realtime, pas le 3B 2507. CUDA + Metal + CPU sur AMD Windows = **CPU only**. Embeddable en serveur HTTP OpenAI-compatible comme Ollama. *Easy si on accepte CPU + 4B Realtime* : 3-5 jours pour PoC. Mais qualité 4B Realtime non confirmée et 7900 XT inutilisé. Louis a explicitement noté vouloir tester *« même si c'est que sur CPU »* — *« si ça fonctionne bien sur CPU, comme ça on est sûr de faire tourner ça sur toutes les machines »*. À garder comme banc de test rapide.
- **candle Voxtral**. PR [#3036](https://github.com/huggingface/candle/pull/3036) Voxtral 3B mergée août 2025, backends stables CUDA + Metal seulement. PR ROCm [#3424](https://github.com/huggingface/candle/pull/3424) draft (RDNA3 Linux testé, Windows non spécifié). Issue Vulkan [#3272](https://github.com/huggingface/candle/issues/3272) WIP (Intel Arc testé, pas RDNA3). Long terme (3-6 mois), suivi opportuniste.
- **Ollama Voxtral / Phi-4 audio**. Issues [#9387](https://github.com/ollama/ollama/issues/9387) et [#11798](https://github.com/ollama/ollama/issues/11798) ouvertes, infrastructure audio inexistante côté Ollama. À surveiller passivement.
- **LM Studio**. Wrapper llama.cpp, pas de GGUF Voxtral / Phi-4 mm audio supportés. Confirmation HF discussion #3.
- **vLLM via WSL2**. Linux/CUDA-first, ROCm-on-WSL bleeding edge, ~20 GB containers à distribuer. Pas aligné avec la posture *Microsoft first-party Windows app* documentée dans le `CLAUDE.md` racine. À écarter.
- **Foundry Local**. Catalog audio = Whisper uniquement. Phi-4-multimodal absent. Issue [#718](https://github.com/microsoft/Foundry-Local/issues/718) (errors on AMD device, ouverte 2026-05-21) confirme le terrain AMD rugueux côté Foundry. Routes via Windows ML/MIGraphX EP **explicitement non supportées pour scénarios GenAI** par Microsoft.

# Lectures méta

**Le bug Phi-4 audio est de la dette technique organisationnelle Microsoft, pas un complot.** Pattern observé : modèle correctement exporté (équipe modèle), runtime mal coordonné côté multimodal (équipe runtime), bug détecté ~9 jours après le ship (issue #1290 le 28 février 2025), jamais priorisé. PR #2167 (mai 2026) rend explicitement le speech sub-model *optionnel* — Microsoft sait que l'audio est cassé et offre un opt-out propre plutôt que de fixer. Toute la R&D ASR Microsoft post-Phi-4 va sur d'autres modèles (Whisper redesign, Nemotron, Parakeet, Cohere, Gemma4). Phi-4-mm audio est en *maintenance-only mode* sans le dire publiquement. Retirer le repo HF serait un échec marketing — *damage control* silencieux. Microsoft Research a posé la première pierre (modèle SOTA dans les benchmarks publics), Microsoft Engineering n'a pas suivi côté runtime local.

**La leçon utile pour Deckle** : un export ONNX officiel ≠ une chaîne d'inférence officielle qui marche. Les deux doivent être validés séparément, sur la machine cible, sur le modèle exact, avant de capitaliser. La discipline du palier 1 *« sanity check single-sample avant grande passe »* reste un garde-fou systématique pour tout futur backend ASR.

**Posture d'investigation à conserver pour cette phase** : *« quels chemins sont possibles ? »* plutôt que *« est-ce que ça vaut le coup ? »*. Le go/no-go vient après, sur des chiffres concrets. Pour cette phase d'exploration spécifiquement, les agents recherche doivent décomposer le problème et localiser les angles d'attaque concrets, pas conclure sur la viabilité globale. Posture explicite, à recadrer quand on bascule en phase d'exécution.

# Pointers

- [`research--phi-4-multimodal-state-of-the-art--2026-05-27.md`](research--phi-4-multimodal-state-of-the-art--2026-05-27.md) — fiche amont sur Phi-4-Multimodal (cartographie identité + perf FR publique + écosystème C#).
- [`research--asr-benchmarks-voxtral-vs-whisper-fr--2026-05-27.md`](research--asr-benchmarks-voxtral-vs-whisper-fr--2026-05-27.md) — comparatif ASR FR pour situer Phi-4 vs Voxtral vs Whisper sur benchmarks publics.
- [`research--whisper-alternatives-fine-windowing--2026-05-27.md`](research--whisper-alternatives-fine-windowing--2026-05-27.md) — fenêtrage fin Whisper, prédécesseur du plan Whisper dynamic présenté ici.
- [`benchmark/JOURNAL.md`](../../benchmark/JOURNAL.md) — entrées 2026-05-27 suites 1 à 6 pour la chronologie expérimentale amont.
- [`ADR-0007 — Rester sur whisper.cpp, surveiller Voxtral`](../adr/0007-rester-sur-whisper-cpp-surveiller-voxtral.md) — pour la posture de bascule ASR.
- [`ADR-0008 — Rester sur Vulkan pour les backends GPU natifs`](../adr/0008-rester-sur-vulkan-pour-backends-gpu-natifs.md) — doctrine Vulkan invoquée par la voie 3 TrevorS.
- [`ADR-0010 — Backend ASR pluggable via IAsrBackend`](../adr/0010-backend-asr-pluggable-via-iasrbackend.md) — pattern parent/enfant pour l'intégration d'un nouveau backend (Phi-4, Voxtral, etc.).
- [`ADR-0016 — Inférence safetensors-native pour Voxtral`](../adr/0016-inference-safetensors-native-pour-voxtral.md) — voie active du POC Voxtral via Transformers + torch ROCm Windows, parent du POC actuel.
