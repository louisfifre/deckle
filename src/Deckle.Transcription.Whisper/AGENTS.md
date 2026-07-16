---
description: whisper.cpp ASR backend (IAsrBackend) — native log compaction, the whisper repetition guard, runtime provisioning, and the prompt/threshold pitfalls.
type: agent-instructions
---

# AGENTS.md — Deckle.Transcription.Whisper

The whisper.cpp `IAsrBackend` implementation (`WhisperBackend`), wrapping the whole P/Invoke layer to `libwhisper.dll`. Child of `Deckle.Transcription` with no reverse reference — the parent never sees it; `Deckle.App` instantiates the backend and injects it into the engine.

## Native log compaction

whisper.cpp emits a flood of native log lines, unrelated to the EventSource pipeline. A process-global `whisper_log_set` hook intercepts them, compacts the four tracked init/model-load/backend/state phases into structured events, and sniffs the first `ggml_vulkan:` / `ggml_cuda:` / `ggml_metal:` line to set `DetectedAccelerator` (no match → CPU). The native hook and its managed delegate stay rooted for the process lifetime; a weak owner routes observations to the latest live backend without retaining a disposed instance.

## Repetition guard

`RepetitionDetector` catches a whisper-specific failure: on long audio with ambiguous trailing silence, the greedy decoder loops at `p̂ ≈ 0.99`, where `logprob_thold` and `entropy_thold` don't bite. Two shapes are guarded on a strict character-exact match — one phrase repeating (`A A A`, the observed 2026-04-18 case) and an alternating pair (`A B A B`). The strict match keeps a legitimate refrain from tripping it; the abort is non-destructive — it stops the runaway decode through the `abort_callback` and keeps the segments produced so far. It lives here, not in the parent — another backend will fail differently.

## Native runtime

`libwhisper.dll` and the ggml backends (Vulkan first, CPU fallback) are not in the repo — downloaded at first-run from the `native-vX.Y.Z` GitHub release, or recompiled by the maintainer on an upstream bump. A `SetDllImportResolver` loads them from `<UserDataRoot>\native\`, not the exe directory. The `EntryDll` constant must stay in sync with the literal in every `[DllImport("libwhisper")]` — C# requires a literal there, so the duplication is unavoidable.

## Whisper initial prompt

Whisper is not instruction-tuned: `initial_prompt` is a stylistic sample to imitate, not an instruction. Meta phrases ("here is a transcription…") leak into the output. Never put a raw-oral→clean example in it — Whisper emits a single text, so the prompt only shows what a clean output looks like.

## Threshold and log-level pitfalls

- **`entropy_thold` is inverted**: the fallback test is `entropy < threshold`, so a HIGH value is STRICT (re-decodes more often), a LOW one permissive. Re-read before retuning.
- **`ggml_log_level` defies intuition**: `NONE=0, DEBUG=1, INFO=2, WARN=3, ERROR=4, CONT=5`. Whisper's routine model-load and `whisper_full` lines emit at INFO=2 — map ERROR→error, WARN→warning, else→verbose, or every normal line floods as a warning.
