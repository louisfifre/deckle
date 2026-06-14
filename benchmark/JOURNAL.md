---
description: Dated findings from the Voxtral/ASR benchmark spike — backends, quantization, and the bench bugs found.
type: module-journal
---

# Journal — Benchmark

Dated discoveries from the ASR benchmark spike. Findings, not plans; where a finding hardened into doctrine it moved to a CLAUDE.md or CONTEXT.md. Most recent on top.

## 2026-06-14 — Local French TTS audition: Chatterbox in, Orpheus out

Spike auditioning local ONNX French TTS by ear (`benches/tts-audition/`). Kept: **Chatterbox-Multilingual** (Resemble AI, MIT). Zero-shot voice cloning — timbre comes from a reference clip, the model re-renders it with far more natural prosody. Feeding a French Piper VITS clip (UPMC Pierre/Jessica) as reference gives a high-quality French voice; the shipped `default_voice.wav` is English, which had carried an anglo accent. Flatness/control is **temperature** (LM sampling), not `exaggeration` — exaggeration is near-inert on the multilingual model (emotion_adv_fc crushed by RMSNorm, chatterbox#355); top_k/cfg_weight aren't reachable from the hand-rolled ONNX decode. Piper VITS itself has no temperature (fixed noise_scale). ~real-time on CPU.

Dropped: **Orpheus FR** (canopylabs/3b-fr-ft-research_release — the gated production finetune; kadirnar/Orpheus-Cml-FR is the wrong, base-finetuned fork). Ran it locally on the RX 7900 XT via onnxruntime-genai-directml (genai 0.13.1 + ort-directml 1.24.4 — the 0.14.x pairing needs ort-directml ≥1.26, which doesn't exist; fp16 export ~6.5 GB, ~0.72× realtime). Fixes found: tokenize via the `tokenizers` lib (genai 0.13.1 rejects the transformers-5 `TokenizersBackend` class); canonical prompt `[SOH]+[BOS]+tokenizer("voice: text")+[EOT,EOH]`, the model emits the `[SOA][SOS]` speech-start itself (pre-filling them → short/empty turns); `eos_token_id` → `[128258,128009]`; the laion `snac24_int2wav_static.onnx` is fixed at 12 frames and must be driven as a sliding-centre window (hop 10, keep `[2048:22528]`, edge-pad) — disjoint blocks lose conv context and garble past ~1 s. Even so the output stayed incoherent (inconsistent length, truncation/rambling) and the `<laugh>/<sigh>` tags are read literally on this finetune — its one differentiator. Not viable; abandoned.

## 2026-05-28 — Voxtral ONNX/DirectML (Python): FP16 over Q4F16

A Python ORT-DirectML pipeline transcribed cleanly in FP16 with the canonical transcription prompt `<s>[INST][BEGIN_AUDIO][AUDIO]×N[/INST] lang:fr [TRANSCRIBE]` (distinct from the bundle's `chat_template.jinja`, which omits `[BEGIN_AUDIO]` and puts the instruction inside the INST block — mixing the two produces gibberish). On one 30s sample, Q4F16 showed a visible hallucination ("Yaka") and an invisible grammar slip ("moi qui fais" → "qui fait") that FP16 didn't — one sample, consistent with the Q4 fidelity cost below, not a quantified verdict. Perf blocker found: KV-cache decode degenerates at step 1 while no-KV (O(N²)) is correct; untested lead is `position_ids` from a masked cumsum rather than a plain arange.

## 2026-05-28 — voxtral-burn builds native on RX 7900 XT (Vulkan)

The Burn/CubeCL/wgpu runtime built and ran on Windows + RX 7900 XT over Vulkan — no Python, no ROCm. RTF ~0.36 on long-form (auto-chunked 12s). The Q4 4B Realtime model showed word-tail truncation on short clips that the 3B 2507 BF16 didn't. Env trap: the VS 2026 install is incomplete (`lib\onecore` only, no desktop `lib\x64` or `include`), so the MSVC-toolchain Rust build fails at link (`msvcrt.lib`, then `vcruntime.h`); built with `rust-gnu` (MinGW) instead.

## 2026-05-27 — Phi-4 multimodal audio is broken in onnxruntime-genai

Phi-4-multimodal ONNX via OGA returns refusals ("I can't transcribe…") on every prompt variant tried — it sees no audio. Upstream bug [onnxruntime-genai#1455](https://github.com/microsoft/onnxruntime-genai/issues/1455) (OPEN): OGA doesn't inject the audio embeddings into the LM; the transformers/torch build of the same model works. Blocked upstream, nothing Deckle can patch trivially.

## 2026-05-27 — Long-audio truncation was a bench bug, not the model

Voxtral looked like it truncated long transcriptions mid-sentence — which had weighed against it versus Whisper. Found the cause in the bench: `max_new_tokens_per_audio_s = 4.0` (floor 128) capped a 39s audio at ~158 tokens against a ~200-260 token reference. Raised to 8.0; the truncations cleared and WER dropped on the long audios with no regression elsewhere. The disqualifying defect was ours, so the Whisper-vs-Voxtral comparison reopened.

## 2026-05-27 — llama-mtmd-cli paraphrases instead of transcribing

`llama-mtmd-cli` has no pure transcription mode and doesn't inject the `[TRANSCRIBE]` token — it falls back to a chat template. On short or silent clips the model comments or paraphrases (a 1.7s "…douter un peu." came out as a 200-word philosophy essay). The Mini 3B is more sensitive than the Small 24B. The Transformers path (`apply_transcription_request`) injects the token implicitly and doesn't show this. Structural to the runtime, not the quantization.

## 2026-05-27 — Q4 quantization costs French fidelity (measured internally)

On the private corpus, BF16 Mini 3B beat Q4_K_M Small 24B on French nuance — pronouns (je/tu), suffixes (0.3.1 → 0.3), EN technical terms — at 5× fewer params, and was far more stable (WER stdev ~24× lower). Consistent with Cohere [arXiv 2407.03211] (−16.6% human-perceived FR degradation FP16 → 4-bit vs −0.3% on automatic metrics), without being proof. The manifest-vs-invisible reading from this corpus — an invisible plausible substitution (Whisper "Halloween") is worse than a visible wrong word (Voxtral "low window") — fed the CONTEXT.md fidelity criteria.

## 2026-05-27 — transformers must stay <5.0 on the AMD ROCm wheel

`transformers 5.x` reintroduces the `torch.distributed.tensor` import (via `continuous_batching`); the AMD `torch …+rocm` Windows wheel is built `USE_DISTRIBUTED=0` and crashes at import of `VoxtralForConditionalGeneration`. Pin `transformers >=4.56,<5.0`. Trail: an earlier May diagnostic had written off ROCm-on-Windows over this same import — but it had been guarded upstream months before ([transformers#40038](https://github.com/huggingface/transformers/pull/40038), merged 2025-08-12); the diagnostic aged silently, and the DirectML pivot it triggered wasn't needed.

## 2026-05-27 — Bench bugs that corrupted runs silently

- **Silent run overwrite.** `next_run_id` required `[a-z0-9]+` (no dash), so any source name with a dash (`voxtral-transformers`) never matched, always returned 1, and write-mode truncated the existing run. A prior BF16 run was lost this way. Parsed via the `{model}-{phase}-` prefix + `\d{4}$` now.
- **Judge prompt desync.** After the regimes moved V1-V5 → T1-T6, the Gemini judge rubric still listed the old names; the judge improvised on the unknown names and produced coherent-looking but meaningless paralinguistic axes (WER unaffected). Any regime refactor must re-check the judge and metric prompts.

## 2026-05-27 — Ecosystem state for Voxtral on Windows AMD (as of late May)

No official ONNX export for Voxtral. No viable native C++/Rust runtime on Windows AMD except llama.cpp: mistral.rs is CUDA/Metal only and doesn't cover 3B 2507; candle supports the model but has neither Vulkan nor ROCm-Windows. Whisper.cpp large-v3 (the deployed fallback) is stable and fast, with limits seen in prod: hallucinations on near-silence ("Sous-titrage Société Radio-Canada"), slow VAD, occasional looping on long dictation.
