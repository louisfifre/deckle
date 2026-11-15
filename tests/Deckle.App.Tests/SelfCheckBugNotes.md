# Self-check — bug notes

## A machine with no microphone failed the self-check, so deploy-prod refused to stage main

- **Trigger:** a full self-check run (as `scripts/deploy-prod.ps1` launches it) on a machine where Windows exposes no capture device.
- **Symptom (observed on 2026-11-14 and 2026-11-15, for v0.129.0 and v0.130.0):** the report carries the warning `microphone: device_missing (E_NOTFOUND); the native library loaded.` and also the failure `1 log event(s) at Error or Critical during the run: MicrophoneOpenFailed`, so the outcome is `failed` and the prod copy is not updated.
- **Cause:** `MicrophoneCapture.Probe` began logging its refusal as `MicrophoneOpenFailed` (Error) with commit 6e60fe8a, after the bug note `TakeStartCheckBugNotes.md` asked for the stage and the code of a refused probe. The self-check verdict fails on any Error in the run's log, and its comment still said the probe logs nothing of its own. So the probe's line turned a missing device, which the verdict only warns about, into a failure.
- **Violated invariant:** the self-check judges the build, not the machine: a missing, busy or refusing microphone is a warning. It is stated where the verdict forgives the probe's line, in `SelfCheckReport.Judge` (src/Deckle.App/SelfCheck/SelfCheckReport.cs).
- **Recurrence cue:** a new log line at Error written by a component the self-check probes, for a condition the verdict already classifies as a machine fact; a self-check that fails with a warning and an Error that name the same device.
- **Guarded by:** `SelfCheckTests.Verdict_PassesWhenTheOnlyErrorIsTheProbeReportingAMachineFact`, `Verdict_StillFailsOnTheProbesErrorWhenTheLibraryDidNotLoad` and `Verdict_StillFailsOnAnotherErrorBesideAMissingMicrophone`.
- **Uncertain:** whether a missing device should be logged at Error at all outside the self-check. Left as it is; it is a question for `deckle-logging`, not for this fix.
