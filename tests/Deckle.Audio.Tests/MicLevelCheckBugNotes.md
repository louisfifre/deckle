# Mic level check bug notes

## A muted microphone was told to turn the pre-processing on

- **Trigger:** the Recording page mic check measures a capture that is entirely zeros (a muted or dead input that still delivers frames).
- **Observed symptom:** none observed on a real device. Found on 2026-10-22 by a code audit and reproduced by a test: the check reported a signal at -120 dBFS and advised `Recommended`, so the page would have shown a boost recommendation instead of its "could not read the mic" state. Whether a given driver delivers exact zeros when muted was not checked.
- **Cause:** `MicLevelCheck.Assess` guarded only a null or empty buffer. A zero-filled buffer went through the DSP, whose RMS helper floors it at -120 dBFS, so the deficit to the target read as roughly 100 dB and `HasSignal` was hard-coded true.
- **Resolution (2026-10-22):** an input level at the -120 dBFS floor now returns `HasSignal = false` with `NotNeeded`, the same result as an empty capture, which `RecordingViewModel` already turns into the error state. A very quiet but non-zero capture is unchanged.
- **Violated invariant:** the check gives no verdict on a capture in which nothing was heard (its own header says `HasSignal` is false on silence).
- **Recurrence cue:** a level computed with a floor constant (`-120`) that is then compared against a target as if it were a measurement.
- **Regression coverage:** `MicLevelCheckTests.ADigitallySilentCaptureHasNoSignalAndNoRecommendation` (regression); `AVeryQuietButNonZeroCaptureStillHasASignal` pins the other side.