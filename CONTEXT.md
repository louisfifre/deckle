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

## Input — contacts and gestures

Vocabulary of the trackpad workstream. The chain reads bottom-up: the device emits reports, the input layer assembles contact frames, the recognizer turns frames into intentions.

**Contact frame** :
The complete snapshot of touchpad contacts assembled from one Raw Input read — per finger an identifier and a position, plus the device's own contact count and scan time. The unit the recognizer consumes. Reassembled when the device fragments it across several HID messages.
_Avoid_ : report (the HID transport message, possibly partial), sample.

**Recognizer** :
The state machine that turns the stream of contact frames into gesture intentions — drag start, drag continuation, release. Owns every quality-defining decision: tap vs drag, the grace delay on finger lift, robustness to Bluetooth report cadence. Reads what the frame states (contact count) rather than inferring from inter-frame silence.
_Avoid_ : detector, gesture engine.

**Three-finger drag** :
The one gesture Deckle owns — three fingers moving together hold the primary button and drag; lifting releases after a grace delay. Every other touchpad gesture stays native to Windows; the native three-finger gestures are set to nothing so this one can exist.
_Avoid_ : three-finger swipe (the native Windows gesture Deckle disables).

## Autocorrect — where correction may act

Vocabulary of the system-autocorrect workstream (machine-wide diacritics restoration first, conservative typo correction second).

**Correctable surface** :
A text-input context where the system autocorrect is allowed to act. Two gates, both required. First, the *surface class* must be correctable: password fields are outside the system entirely — never corrected, keystrokes never observed or buffered (hard rule, not a setting) — and non-prose contexts (terminals, code editors, full-screen games) are excluded by default. Classes are judged per surface (the focused control), not per application: an embedded terminal inside an otherwise prose app is still excluded, to the extent the surface can be identified. The exact class inventory follows the background research. Second, the *application* must be enrolled.
_Avoid_ : blocklist-only framing (exclusion classes are the safety net, enrollment is the activation gate — they are different gates).

**Enrolled app** :
An application where autocorrect is active because the user accepted the enrollment prompt there. Enrollment is asked once per app and remembered; an app never asked, or never answered, stays non-enrolled and untouched.

**Enrollment prompt** :
The notification raised the first time the system *could* correct something in a non-enrolled app — never on mere app launch, so apps where no prose is ever typed never see it. It does not block, steal focus, or rewrite anything; corrections stay withheld until the user opts in, and ignoring the prompt is a valid answer. Candidate refinement, noted not committed: applying the withheld corrections retroactively when consent arrives late.
_Avoid_ : popup, dialog (it is a passive notification, not a modal).

**Correction revert** :
The single gesture that takes a correction back: the Backspace that deletes the space sitting right after a corrected word also restores the original word — whether it comes immediately after the correction fired, or later, after moving the caret back to that spot. Backspaces beyond that one delete normally, into the text before. Ctrl+Z is never intercepted — it belongs to the apps. Kept deliberately simple by decision; open questions: whether a space is re-added after restoring, and what removing letters *inside* a corrected word should do.
_Avoid_ : undo (Ctrl+Z semantics).

**Personal dictionary** :
The user-visible surface of everything autocorrect has learned — adopted words and suppressed corrections. Inspectable and editable by principle: a consultable list, per-word removal, full purge. Suppression is an explicit entry (a blocklist), never the mere erasure of a counter — a removed word must not come back on its own. Candidate bridge to the ASR personal lexicon (shared learning, not yet committed).
_Avoid_ : learning store (the internal mechanism; this term names the visible surface).

## Speech segmentation — detection vs cutting

Two devices carry the word "VAD" and are constantly conflated, yet they are different in kind: one is a model that finds speech in a finished buffer, the other is a threshold that cuts a live stream. They also produce two different units of "cut", which must not be confused.

**Neural VAD** :
A neural-network voice-activity detector (today Silero) that runs over a whole captured buffer to find speech regions and trim silence before decoding. Model-based and post-hoc — it needs the full buffer in hand. Wired inside `whisper_full` via `vad_model_path`.
_Avoid_ : VAD (bare — ambiguous), Silero (vendor-specific; the term should outlive the model).

**Energy segmenter** :
A threshold-on-RMS state machine that runs live on the capture's real-time energy stream to place **utterance** boundaries at silences. Not a model and not speech recognition — it reads energy dips to decide where to cut, feeding the streaming producer/consumer pipeline.
_Avoid_ : energy VAD (it does not detect voice, it cuts on silence), VAD.

**Utterance** :
The unit the energy segmenter emits — a speech span bounded by detected silence (or by the safety ceiling), and the atomic audio chunk handed to the backend for one transcription call.
_Avoid_ : chunk, segment.

**Segment** :
Whisper's *output* unit — a short timestamped span the model re-derives **inside** each utterance from its own decoding. An utterance is an input cut decided by energy; a segment is an output cut decided by Whisper. One utterance contains one or more segments.
_Avoid_ : utterance (the input unit), window (the 30 s encoder input).
