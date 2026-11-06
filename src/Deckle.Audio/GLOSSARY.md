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

## Voice level and voice detector

**Voice level** :
The one level, in dBFS, above which Deckle hears voice in a 50 ms frame of the microphone (`CaptureSettings.VoiceLevelDbfs`, Settings > Recording). It is the exit level of the voice detector. The energy segmenter cuts on it, the low-audio warning tests it, and the display level is anchored on it, dark at the voice level.
_Avoid_ : threshold (in anything a user reads), speech level (the former name), detector confidence (the neural VAD's probability, a different signal and a different scale).

**Voice detector** :
The small stateful object (`VoiceDetector`) that, fed one frame's RMS at a time, says voiced or silent. A silent frame must reach the entry level to open a voiced run, and a voiced run ends only below the voice level itself. The entry margin between the two is 0 by default, so the entry is the voice level; a positive margin, still to be sized, would gate only the opening of an utterance, since the resume after a pause (`Resume`, which the segmenter asks while an utterance is open) is at the voice level. Each consumer runs its own, on the same level and the same RMS.
_Avoid_ : VAD (bare, ambiguous with the neural VAD), energy VAD.

**Voice meter** :
The instrument under the voice level on the Recording page (`VoiceLevelMeter`). While Listen is on it draws the microphone's live level on a fixed dB track, marks the voice level (and the detector's entry level, only when the entry margin is positive), and fills in the accent while its own voice detector says voiced. It shows and never writes: nothing is recorded, nothing persists, and the voice level is changed only in its field.
_Avoid_ : level meter (bare, it says nothing of the voice level it is read against), VU meter (a different ballistic and scale).
