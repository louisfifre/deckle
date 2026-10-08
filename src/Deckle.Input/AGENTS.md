---
description: Input support — Raw Input hosts, touchpad HID parsing, injection.
type: agent-instructions
---

# AGENTS.md — Deckle.Input

This module reads raw input and injects pointer input. It knows no gesture: domain modules such as Trackpad own the gestures. The vocabulary (contact frame, recognizer) is normative in `CONTEXT.md` § Input.

## Doctrine

- **Dedicated input thread.** The Raw Input host owns its own message-only window and pump on a dedicated thread. Frames feed a latency-sensitive injection path that a busy UI frame must not stutter. Keep it separate from `Deckle.Shell`'s MessageOnlyHost.
- **Standard read, never buffered.** Register with `RIDEV_INPUTSINK` + `RIDEV_DEVNOTIFY`. `GetRawInputBuffer` is incompatible with `RIDEV_DEVNOTIFY`: never introduce it here.
- **Finger presence is data, not inference.** The parser reads the tip switch and confidence bits per contact (`HidP_GetUsages`). Finger lifts are read, never guessed by timing; reference implementations that read value caps only fall into that trap.
- **Never log frames one by one.** At report cadence they drown every sink.

## Attribution

HID parsing follows `emoacht/RawInput.Touchpad` (MIT), with hybrid-mode frame reassembly per the Microsoft Precision Touchpad spec. See the repository root `NOTICE.md`.
