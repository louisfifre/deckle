---
description: Input support module — Raw Input host, Precision Touchpad HID parsing, contact frames, SendInput primitive. No gesture knowledge.
type: agent-instructions
---

# CLAUDE.md — Deckle.Input

Support layer of the input pillar: everything that reads raw device input
and synthesizes pointer input, with zero knowledge of gestures. Domain
modules (Deckle.Input.Trackpad first) consume the contact-frame stream
and the injection primitive. The split mirrors Lighting /
Lighting.Ambient. Vocabulary (contact frame, recognizer) is normative in
`CONTEXT.md` § Input.

## Doctrine

**Dedicated input thread.** The Raw Input host owns its own message-only
window and message pump on a dedicated thread — contact frames arrive at
report cadence and feed an injection path whose perceived quality is
latency; a busy UI frame must not stutter a drag. `Deckle.Shell`'s
MessageOnlyHost stays the home of low-rate hotkey/tray plumbing; the two
do not merge.

**Standard read only.** Registration uses `RIDEV_INPUTSINK` (frames
regardless of focus) plus `RIDEV_DEVNOTIFY` (Bluetooth arrivals and
removals). The buffered `GetRawInputBuffer` read is documented as
incompatible with `RIDEV_DEVNOTIFY` — never introduce it here.

**Read what the frame states.** The parser reads the tip switch and
confidence bits per contact through the HID button path
(`HidP_GetUsages`) — the reference implementations only read value caps
and therefore never see finger lifts, which forces them into timing
heuristics. Finger presence is data, not inference; keep it that way.

**Frames are never logged individually.** At report cadence they would
drown every sink. The EventSource (`Deckle-Input`) carries lifecycle
milestones, device presence, and a periodic rollup; raw frames go to the
dedicated JSONL recorder under `telemetry/` when the diagnostics toggle
asks for them.

**Preparsed data is cached per device** and invalidated on device
change — never re-fetched per frame.

## Attribution

The HID parsing approach follows `emoacht/RawInput.Touchpad` (MIT), with
the hybrid-mode frame reassembly rule from the Microsoft Precision
Touchpad collection spec. See `NOTICE.md`.
