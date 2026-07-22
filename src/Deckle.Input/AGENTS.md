---
description: Input support module — Raw Input hosts, Precision Touchpad HID parsing, and native injection primitives. No gesture knowledge.
type: agent-instructions
---

# AGENTS.md — Deckle.Input

Support layer of the input pillar: it reads raw device input and synthesizes pointer input, with zero knowledge of gestures. Domain modules (Trackpad first) consume the contact-frame stream and the injection primitive; the split mirrors Lighting / Lighting.Ambient. Vocabulary (contact frame, recognizer) is normative in `CONTEXT.md` § Input.

## Doctrine

**Dedicated input thread.** The Raw Input host owns its own message-only window and pump on a dedicated thread — frames arrive at report cadence and feed a latency-sensitive injection path that a busy UI frame must not stutter. It stays separate from `Deckle.Shell`'s MessageOnlyHost.

**Standard read, never buffered.** Registration is `RIDEV_INPUTSINK` + `RIDEV_DEVNOTIFY`; `GetRawInputBuffer` is incompatible with `RIDEV_DEVNOTIFY` — never introduce it here.

**Finger presence is data, not inference.** The parser reads the tip switch and confidence bits per contact (`HidP_GetUsages`), so finger lifts are read, not guessed by timing — the trap the value-caps-only reference implementations fall into.

**Frames are never logged individually** — at report cadence they drown every sink.

## Attribution

HID parsing follows `emoacht/RawInput.Touchpad` (MIT), with hybrid-mode frame reassembly per the Microsoft Precision Touchpad spec. See `NOTICE.md`.
