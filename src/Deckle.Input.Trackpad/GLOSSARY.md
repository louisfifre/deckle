---
name: glossary-deckle-input-trackpad
description: "Trackpad gesture vocabulary. It defines the recognizer, the state machine that owns gesture quality, and the three-finger drag, the one gesture Deckle owns. Read before touching gesture recognition."
---

# Deckle.Input.Trackpad, Glossary

Vocabulary of the trackpad workstream. This module sits at the top of the input chain. It consumes the contact frames assembled by `Deckle.Input` and turns them into gesture intentions.

## Gestures

**Recognizer** :
The state machine of this module that turns the stream of contact frames into gesture intentions, such as drag start, drag continuation and release. It owns every decision that makes a gesture feel right, such as telling a tap from a drag, the grace delay when a finger lifts and the tolerance to the cadence of Bluetooth reports.
_Avoid_ : detector (signals a presence, the recognizer interprets), gesture engine (a loose name for the same component).

**Three-finger drag** :
A gesture, the only one Deckle owns, in which three fingers moving together hold the primary button and drag, and lifting them releases after a grace delay. Every other touchpad gesture stays native to Windows.
_Avoid_ : three-finger swipe (the native Windows gesture Deckle disables).

**Detector** :
In ordinary use, a component that signals that something is present, as a voice-activity detector does. The component that interprets contact frames as gestures is the recognizer.

**Gesture engine** :
A loose name for whatever handles gestures. Deckle calls the component that turns contact frames into gesture intentions the recognizer.

**Three-finger swipe** :
A gesture native to Windows, in which three fingers swipe to switch apps or show the desktop. Deckle disables it to make room for the three-finger drag.
