---
description: Trackpad domain module — three-finger drag and its frozen decisions.
type: agent-instructions
---

# AGENTS.md — Deckle.Input.Trackpad

- **The Precision Touchpad driver is an external prerequisite.** Without it no multi-touch data reaches userspace. Deckle depends on it, never replaces it.
- **V1 is the three-finger drag alone.** Other gestures stay native; the native three-finger gestures are set to nothing. A three-finger tap and a click during a drag are deliberately nothing: one gesture, one intention.
- **Injection is relative `SendInput`.** Touch injection, virtual HID and `WM_NCLBUTTONDOWN` were evaluated and rejected.
- **Lifts are read, never timed.** Read them from the contact count and tip switches: a timing heuristic causes the reference implementation's Magic Trackpad issues. The grace delay on lift resumes the same drag.
- **Sensitivity is one linear slider**, no home-grown acceleration: relative injection already rides the user's Windows pointer curve.
- **All values are constants in `TrackpadEngine`**; the slider is the only knob. Anti-jump clamping is never exposed. A stale `Tuning` object in `settings.json` is ignored.
- **The start threshold is not zero**: at zero a three-finger tap would become a left click.
- **Elevation is an opt-in scheduled task**, off by default. Without it, UIPI silently blocks the drag on elevated windows.
- **Neutralizing the Windows gestures backs up the registry values** and can be restored. Whether it applies live is unverified.
- **Never "Remove device" in Bluetooth settings.** Power-cycling the trackpad is enough.
