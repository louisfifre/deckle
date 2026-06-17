---
description: Trackpad domain module — three-finger drag recognizer and engine, module settings and page, one-click acts. Frozen framing decisions live here.
type: agent-instructions
---

# AGENTS.md — Deckle.Input.Trackpad

Domain module bringing the Mac gesture vocabulary to the Magic
Trackpad 2 on Windows — three-finger drag first, replacing the
third-party ThreeFingerDragOnWindows. Consumes `Deckle.Input` (contact
frames in, injection primitive out); owns the recognizer, the drag
engine, the module settings and page, and the one-click acts.

A Precision Touchpad driver for the device is an **external
prerequisite** — without it the trackpad is a plain HID mouse and no
multi-touch data reaches userspace. Deckle depends on that driver layer,
never replaces it.

## Framing decisions (grill session 2026-06-12)

**V1 scope is the three-finger drag alone.** Every other gesture stays
native to Windows; the native three-finger gestures are set to nothing
so this one can exist. A three-finger tap is deliberately nothing; a
mechanical click during a drag is deliberately nothing — one gesture,
one intention.

**Injection is relative `SendInput`,** chosen after explicit evaluation
of touch injection (gesture interpretation not guaranteed, breaks
`DoDragDrop`), virtual HID (signed kernel driver, same ambiguities) and
`WM_NCLBUTTONDOWN` (window chrome only). Injection latency is a
non-subject (sub-millisecond syscall); the perceived quality lives
entirely in the recognizer.

**The recognizer is Bluetooth-first.** Finger lifts are read from the
contact count and tip switches in the frames, never inferred from
inter-frame silence — the reference implementation's 40 ms timing
heuristic is the documented cause of its Magic Trackpad issues. The
grace delay on lift resumes the same drag without releasing the button.
Anti-jump clamping is an in-engine constant, never exposed.

**Sensitivity is one slider** — a linear multiplier before injection,
zero home-grown acceleration: relative injection already rides the
Windows pointer curve the user calibrated.

**Elevation is an opt-in scheduled task** ("start elevated", PowerToys
pattern, one UAC at activation), off by default and never imposed.
Without it, UIPI silently blocks the drag on elevated windows — the
trade-off of an all-elevated Deckle is the maintainer's accepted choice;
a separate elevated host process is the fallback refinement if it ever
itches.

**Acts keep the user sovereign.** Neutralizing the Windows three-finger
gestures backs up the previous registry values and restoring them is
always possible; whether the touchpad stack applies the change live is
unverified (it may require a reconnect). The Bluetooth repair act
encodes the proven driver-store procedure; its cardinal rule: never
"Remove device" in Bluetooth settings — power-cycling the trackpad is
always enough.

## Values frozen (hands-on calibration, 2026-06-12)

All recognizer/engine values are constants in `TrackpadEngine`; the only
user-facing knob is the drag-speed slider. Grace delay 0 — the drag
releases the instant the fingers lift, the maintainer's preference.
Start threshold 0.1 % of the pad width — the calibrated minimum,
perceptually instant; deliberately not zero, because at exactly 0 the
drag would commit without any movement and a three-finger tap would
become a left click, contradicting "tap is nothing". Base scale 0.25.
The tuning expander and the `TrackpadTuning` settings block are gone; a
stale `Tuning` object in an existing `settings.json` is ignored.
