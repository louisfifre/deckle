---
name: glossary-deckle-input
description: "Input-layer vocabulary. It defines the contact frame, the unit assembled from one Raw Input read, and its place in the chain that goes from device report to contact frame to gesture intention. Read before touching touchpad parsing or frame assembly."
---

# Deckle.Input, Glossary

Vocabulary of the input layer. The chain reads from the bottom up. The device emits reports, the input layer assembles them into contact frames, and the recognizer of `Deckle.Input.Trackpad` turns frames into gesture intentions.

## Contacts

**Contact frame** :
A complete snapshot of the touchpad contacts, assembled from one Raw Input read, that gives each finger an identifier and a position, plus the device's own contact count and scan time. It is the unit the recognizer consumes. Unlike a report, it is always whole, even when the device splits it across several HID messages.
_Avoid_ : report (the HID transport message, possibly partial), sample.

**Report** :
In HID, the message a device sends over its transport. A report may carry only part of the contacts, so it is not a contact frame.

**Sample** :
In ordinary use, one measurement taken at one instant, and in Deckle's audio vocabulary one value of the signal. A touchpad snapshot is a contact frame.
