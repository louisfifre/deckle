---
name: context-deckle-input
description: "Input-layer vocabulary — the contact frame as the unit assembled from Raw Input reads, and its place in the report → frame → intention chain. Read before touching touchpad parsing or frame assembly."
type: agent-instructions
---

# Deckle.Input — Context

Vocabulary of the input layer. The chain reads bottom-up: the device emits reports, the input layer assembles contact frames, the recognizer (in `Deckle.Input.Trackpad`) turns frames into intentions.

## Contacts

**Contact frame** :
The complete snapshot of touchpad contacts assembled from one Raw Input read — per finger an identifier and a position, plus the device's own contact count and scan time. The unit the recognizer consumes. Reassembled when the device fragments it across several HID messages.
_Avoid_ : report (the HID transport message, possibly partial), sample.
