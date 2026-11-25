# VAD bug notes

## A failed model download was final for the session

- Trigger: the Silero VAD model is missing on first use and the download fails once, for a network that is not up yet, a DNS hiccup or a host that is briefly down.
- Symptom: every take of the session ran untrimmed, and the log said once that the speech trim was off. Nothing tried again until the next start of the app.
- Cause: `VadService` started the download once (`_downloadKicked`) and treated its failure as the end of the matter.
- Violated invariant: a degradation is said once, where the person can see it, and its end is said too.
- Fix: none by retrying. A retry ladder (30 s, 2 min, 10 min) was added for this entry, then removed on purpose on 2026-11-25: the model is downloaded once per session, in the background, off every caller, so `EnsureReady` still returns at once. A failure is one incident, `download_failed`, shown in the tray, and the next start of the app tries again. A success writes `SpeechTrimVadDownloadComplete` and opens no incident.
- Recurrence cue: a `download_failed` line followed by a second download attempt in the same session, or a tray that stays silent while takes run untrimmed.
- Guard: `VadServiceSingleDownloadTests`, which replace the download with a fake: one failed try opens one incident, a later take starts no second download, and the tray shows the failure but not a download under way.
- Interpretation: the log still shows a `model_missing` Warning before `download_failed`, because the model was absent before the attempt; they are two causes of one stretch without trim.
