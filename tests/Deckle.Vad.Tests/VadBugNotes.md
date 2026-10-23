# VAD bug notes

## A failed model download was final for the session

- Trigger: the Silero VAD model is missing on first use and the download fails once, for a network that is not up yet, a DNS hiccup or a host that is briefly down.
- Symptom: every take of the session ran untrimmed, and the log said once that the speech trim was off. Nothing tried again until the next start of the app.
- Cause: `VadService` started the download once (`_downloadKicked`) and treated its failure as the end of the matter.
- Violated invariant: a degradation that can heal on its own is retried, and the log shows both the degradation and its end.
- Fix: the background download retries after 30 s, 2 min and 10 min (`VadService.DownloadRetryDelays`), off every caller, so `EnsureReady` still returns at once. The failures are one incident: the first opens `download_failed`, each failed attempt is a Verbose detail (`SpeechTrimVadDownloadAttemptFailedDetail`), a success closes it with `SpeechTrimRecovered`, and running out of attempts writes one `download_gave_up` Warning that says the next start will try again. `Dispose` cancels a retry that is waiting.
- Recurrence cue: a `download_failed` line with no later `Speech trim is back on` and no `download_gave_up` line in the same session.
- Guard: `VadServiceDownloadRetryTests`, which replace the download and the wait with fakes.
- Interpretation: the three waits are a judgment (see the comment on `DownloadRetryDelays`), not a measurement. The log still shows a `model_missing` Warning before `download_failed`, because the model was absent before the first attempt; they are two causes of one stretch without trim.
