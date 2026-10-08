---
description: whisper.cpp ASR backend — native log hook, repetition guard, pitfalls.
type: agent-instructions
---

# AGENTS.md — Deckle.Transcription.Whisper

- The native `whisper_log_set` hook and its managed delegate stay rooted for the process lifetime. A weak owner routes native lines to the live backend and never retains a disposed one.
- `RepetitionDetector` lives here, not in the parent: another backend fails differently. It guards a greedy-decoder loop that `logprob_thold` and `entropy_thold` do not catch. A strict character-exact match keeps a real refrain from tripping it. Its abort is non-destructive: it stops the decode through `abort_callback` and keeps the segments produced so far.
- The native DLLs are downloaded, not in the repo. A `SetDllImportResolver` loads them from `<UserDataRoot>\native\`, not the exe folder.
- `EntryDll` must match the literal in every `[DllImport("libwhisper")]`: C# requires a literal there.
- `initial_prompt` is a style sample, not an instruction: Whisper is not instruction-tuned. Meta phrases leak into output. Never put a raw-speech → clean-text example in it.
- `entropy_thold` is inverted: the fallback test is `entropy < threshold`, so a high value is strict, a low one lax.
- `ggml_log_level` does not follow intuition: routine lines arrive at INFO. Map ERROR to error, WARN to warning, everything else to verbose, or normal lines flood as warnings.
