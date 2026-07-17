---
name: context-deckle-input-trackpad
description: "Trackpad gesture vocabulary — the recognizer as the state machine that owns gesture quality, and the three-finger drag as the one gesture Deckle owns. Read before touching gesture recognition."
type: agent-instructions
---

# Deckle.Input.Trackpad — Context

Vocabulary of the trackpad workstream. This module sits at the top of the input chain: it consumes the contact frames assembled by `Deckle.Input` and turns them into gesture intentions.

## Gestures

**Recognizer** :
The state machine that turns the stream of contact frames into gesture intentions — drag start, drag continuation, release. Owns every quality-defining decision: tap vs drag, the grace delay on finger lift, robustness to Bluetooth report cadence. Reads what the frame states (contact count) rather than inferring from inter-frame silence.
_Avoid_ : detector, gesture engine.

**Three-finger drag** :
The one gesture Deckle owns — three fingers moving together hold the primary button and drag; lifting releases after a grace delay. Every other touchpad gesture stays native to Windows; the native three-finger gestures are set to nothing so this one can exist.
_Avoid_ : three-finger swipe (the native Windows gesture Deckle disables).
