---
name: context-deckle
description: "Project glossary for Deckle — shared vocabulary, term-of-art definitions, naming distinctions. Read when a project-specific term needs disambiguation."
type: agent-instructions
---

# Deckle — Context

Glossary of Deckle project terms. Defines the shared vocabulary between Louis and the LLM agents that work on the code. This file captures distinctions that have a concrete reality at Deckle; generic programming concepts do not appear here, unless Deckle gives them a proper internal nuance.

## Testing — layers and categories

Four categories fall within the automatic test scope, runnable by an agent or by Louis via `dotnet test` without human interaction. Two categories are outside the automatic scope: they exist and are useful, but are executed by hand via the `verify` skill.

### In the automatic scope

**unit** :
Test that exercises a type or a function in isolation, without touching the file system, the network, a UI thread, or a native dependency. Natural target: pure leaf modules such as `Deckle.Composition` (ColorSpace, easing, animators), `Deckle.Chrono` (ChronoFormatter), and the pure logic of `Deckle.Core`. This is the largest and fastest layer.

**integration** :
Test that exercises a boundary with a mockable local service. The partner is simulated by a lightweight substitute controlled by the test (test HTTP server for Ollama, temporary file system for `JsonSettingsStore`, audio source simulator for the function that calls the mic). The isolation seam must be *natural* — already present in the architecture or obvious without contortion. A parasitic seam created solely for the test belongs to the "testable but unusable code" drift and is not accepted.
_Avoid_ : end-to-end, e2e (they cover different things elsewhere).

**observability** :
Test that exercises a sequence of EventSource events via an internal `TestEventListener`. Verifies that the code emits the right providers, the right event names, the right levels and keywords, and carries the expected payloads. Category native to Deckle given the weight of the EventSource pipeline (see `src/Deckle.Diagnostics/CLAUDE.md`).
_Avoid_ : log assertion, telemetry test.

**regression** :
Test added in reaction to a specific bug already fixed. Reproduces the conditions of the bug; passes because the fix holds; will fail if the fix is dropped. Its reason for being is to pin the fix in time, not to cover a nominal behavior. A regression test is typically written as a mirror of a `fix(scope): …` commit.

### Outside the automatic scope

**system** :
Test that exercises a heavy native runtime in a realistic condition — loading a 1 GB Whisper model, transcribing a reference audio file stored in the test repo, reading a Hue Entertainment payload on a real bridge. Possible to automate locally, but slow, demanding, and conditional on the availability of native artifacts and hardware. Stays in the hands of Louis or a dedicated workstation.

**interactive** :
Test that requires an interactive Windows workstation and a human or a fake human capable of presenting real conditions to the system — a real mic that picks up sound, a global hotkey that does not conflict with another app, a UIAutomation target window to validate the paste, a physical display for DXGI Output Duplication. Not automatable by an agent. Validated via the `verify` skill.

### Key distinction between integration and system

The boundary between `integration` and `system` plays out on *the weight of the dependency and its substitutability*. The `Deckle.Audio.MicrophoneCapture.Probe` function that queries the audio device for its capabilities falls under `integration` if a fake audio source is substituted behind the WASAPI seam. A test that records 3 seconds of real voice in a complete loop falls under `interactive`. A test that drives Whisper on a wav stored in the test repo falls under `system`.

## Example conversation

> — Le bug d'hier sur le clipboard Win32, on le couvre comment ?
> — Test de regression. Le `OpenClipboard` retournait `false` quand un autre process tenait la session ; la fix retry trois fois ; le test simule trois échecs puis un succès et vérifie qu'on a bien copié.
> — D'accord. Et pour vérifier qu'on émet le bon `ClipboardCopied` à la fin ?
> — C'est de l'observability. Un `TestEventListener` accroché à `DeckleWhispSource`, on assert sur la séquence et sur le payload.
> — Et le micro maintenant ? Je voudrais tester qu'on ne plante pas quand il n'y en a pas.
> — Integration. On simule un device qui retourne « no input » et on vérifie le chemin d'erreur. Un test interactive prendrait un vrai micro débranché — utile mais à la main.

## Transcription — fidelity criteria

The T1 canonical mode (`apply_transcription_request`) is the production transcription path exposed by Deckle. Its output lands in the clipboard for immediate use. The implicit usage criterion is high-volume confidence — Louis must be able to dictate twenty minutes and trust the output without re-reading every line.

The criteria below come from observed failure modes. Each names a class of deviation that discriminates an acceptable transcription from a dangerous one.

**Grammatical number fidelity** :
A singular that becomes plural (or the inverse) can flip an instruction's scope without surfacing as an obvious error. Observed example on audio `701ce47a` : « le contexte minimal 8K » transcribed as « les contextes minimaux 8K » — a targeted instruction turned into a global one. High-severity defect on T1 even when the surrounding sentence is correct.

**Plausible reformulation** :
A transcription error masked by a semantically acceptable substitute, hiding the original mishearing. Observed example on audio `701ce47a` : « à côté » misheard as « en côté » (a non-existent expression), rewritten as « en même temps » — a synonym that fits the context but is unfaithful to the audio. Worse than a visible error because re-reading does not catch it. The hypothesis attached to this pattern is that the chat-mode pass mixes acoustic decoding with a semantic-coherence pressure that overwrites suspicious tokens with plausible neighbors; to be verified.

**Manifest vs invisible error** :
Distinction induced by the plausible-reformulation criterion. A manifest error (typo, non-word, syntactic break) signals itself to the reader. An invisible error (synonym swap, number flip, register shift, name swap) passes the eye and lands in production. Deckle's fidelity criteria prioritize visibility over local polish — a polished output that is invisibly wrong is the worst outcome.

## Audio — display level vs signal pre-processing

Two distinct notions carry the word "level" and must never be conflated: one drives the real-time visual, the other drives the signal actually handed to the transcription engine. They are decoupled by design — display is computed live during capture, signal processing is a terminal transform applied after Stop.

**Display level** :
The perceptual dBFS → [0, 1] mapping produced by `AudioLevelMapper`, calibrated over recent sessions, that drives the intensity of the recording outline while speaking. Concerns the visual render only; never alters the audio. Its calibration lives independently and stays outside the pre-processing scope.
_Avoid_ : gain, volume (those are signal operations, not display).

**Transcription pre-processing** :
A transform of the captured signal (filtering, compression, gain) applied to the `float[]` buffer between `MicrophoneCapture.Record()` and the ASR backend, for the sole purpose of maximizing machine intelligibility — not listening quality. Operates on the samples themselves, downstream of capture and upstream of transcription. Distinct from display level, and independent of how the buffer is windowed for the backend. Implemented as a post-capture two-pass DSP chain in `Deckle.Audio.Preprocessing` (`TranscriptionPreprocessor`); off by default and user-toggled, with a mic level check on the Recording page that advises whether it helps.
_Avoid_ : AGC (it is not real-time automatic gain — it runs once, post-capture), normalization (it is a dynamics chain, not a single peak/RMS scale).
