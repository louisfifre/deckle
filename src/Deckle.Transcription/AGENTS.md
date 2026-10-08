---
description: Transcription orchestrator — ASR boundary, model lifecycle, clipboard rules.
type: agent-instructions
---

# AGENTS.md — Deckle.Transcription

- The orchestrator never touches P/Invoke, native callbacks or C structs. A new ASR backend is a new child module behind `IAsrBackend`.
- Raw transcript text never enters the LogWindow or `app.jsonl`. It lives only in the consented corpus dataset.
- Dependency incidents (microphone, model, VAD, Ollama) persist across takes until real recovery or restart. A take failure closes with its take.
- Never load a model at boot: nothing sits in VRAM while idle. It loads and primes on the first hotkey and unloads after an idle timeout. Capture and the cold prime start concurrently.
- The clipboard holds at most two contents per transcription: the raw text, then the rewritten text. Never write token by token, to keep the clipboard history clean. Granularity is the sentence.
- Paste is off by default. When on, paste only if UI Automation confirms an editable field (`Edit` or `Document`) at Stop. Capture nothing on Start. Do not bring back class-name matching: it misses Chromium, Electron and Qt.
