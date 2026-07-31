---
description: Maintainer verification protocol for live microphone transcription.
type: verification-protocol
---

# Live transcription verification

This protocol covers the hardware and interactive boundaries that deterministic
tests cannot own: the microphone driver, global hotkey, HUD chrono, real Whisper
runtime, Windows clipboard, and the user's actual foreground application.

## Automated evidence first

1. Build `tests/Deckle.Transcription.Tests/Deckle.Transcription.Tests.csproj`.
2. Run `LiveTranscriptionPipelineTests` on a maintainer workstation. It must prove:
   two live utterances reach the shared consumer while capture is open, backend
   calls remain sequential, context carries forward, delivery happens once, and
   the status sequence is Recording → Transcribing → Ready.
3. With the native runtime and a speech model installed, set
   `DECKLE_WHISPER_RUN_SYSTEM=1` and set
   `DECKLE_WHISPER_REFERENCE_EXPECTED` to the human-verified transcript of the
   bundled WAV, then run
   `WhisperReferenceAudioSystemTests`. It must load the real backend and produce
   a successful result within 35% normalized word error rate. Without the opt-in,
   the test must report Skipped rather than Passed.

## Interactive hardware pass

1. Enable the streaming transcription strategy and disable paste.
2. Open a plain editable target such as Notepad, leaving it focused.
3. Start dictation with the production hotkey.
4. Confirm the HUD leaves Charging for Recording only when capture starts, and
   that the chrono advances continuously without resetting.
5. Speak two distinct French phrases separated by a silence longer than the
   configured hangover, then continue speaking briefly before stopping.
6. Stop with the production hotkey.
7. Confirm there is one transition to Transcribing, one terminal Ready, and no
   intermediate reset or second chrono start.
8. Paste manually. Confirm both phrases are present, ordered, and separated as
   two paragraphs; no warm-up or prompt text may appear.
9. Repeat immediately with a short phrase. Confirm the second run starts and
   completes normally, proving the previous worker and cancellation state were
   fully settled.

## Failure pass

1. Select an unavailable microphone device and start dictation.
2. Confirm no recording chrono starts, a localized microphone error appears,
   and the next valid attempt is not locked out.
3. Restore a valid device and repeat the nominal pass.

Record the app version, model, accelerator, input device, observed text, and any
unexpected HUD/status sequence with the verification result.
