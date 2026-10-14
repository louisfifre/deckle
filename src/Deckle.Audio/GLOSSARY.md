---
name: glossary-deckle-audio
description: "Audio vocabulary. It separates display level from transcription pre-processing, the two notions of « level » that must never be conflated. Read before touching level mapping or the DSP chain."
---

# Deckle.Audio, Glossary

Two distinct notions carry the word "level" and must never be conflated. One drives the live visual. The other shapes the signal handed to the transcription engine. They are separate by design. Display level follows the capture live, while pre-processing transforms audio that is already finished.

## Display level vs signal pre-processing

**Display level** :
A displayed level of the signal, mapped from dBFS to a value between 0 and 1 as the ear would hear it, that drives the intensity of the recording outline while the user speaks. It concerns the visual only and never alters the audio.
_Avoid_ : gain (a signal operation, not display), volume (a signal operation, not display).

**Transcription pre-processing** :
A signal transform, such as filtering, compression or gain, applied to the captured audio so that the machine transcribes it more easily, not so that a person hears it better. It acts on the samples themselves, after capture and before transcription. It is distinct from display level and does not depend on how the audio is cut for the engine.
_Avoid_ : AGC (it is not real-time automatic gain, it runs on finished audio, an utterance or a whole take), normalization (it is a dynamics chain, not a single peak or RMS scale).

**Gain** :
An amplification applied to the signal itself, which changes the audio. Display level never applies gain, while transcription pre-processing may.

**Volume** :
In ordinary use, how loud a sound is or is played back. It is a property of the signal or of playback, not of the display.

**AGC** :
Automatic gain control, a real-time adjustment of the gain while the signal is being captured. Transcription pre-processing works on finished audio, so it is not AGC.

**Normalization** :
A single scaling of the whole signal to a target peak or RMS level. Transcription pre-processing is a chain of several dynamics steps.
