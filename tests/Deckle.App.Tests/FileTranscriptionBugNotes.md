# File assistant — bug notes

## An exception in one file stopped the whole batch and could lose its transcript

- **Trigger:** a step of one file throws instead of returning a failure, for example the rewrite's provider call raising an HTTP or JSON error, while the assistant works through several files.
- **Symptom (inferred, found in review on 2026-11-14, not observed by a person):** the file stays on "Rewriting…" with a live spinner, the files after it stay on "Waiting", and the window reports the batch finished. The transcript of that file was only in memory, so it was lost. Quitting Deckle during a rewrite lost it the same way, since the quit only waits for a busy engine and a rewrite leaves the engine idle.
- **Cause:** `FileTranscriptionRun.RunAsync` guarded only the write against `IOException` and `UnauthorizedAccessException`, and wrote the .txt after the rewrite, so anything else thrown left the loop and nothing reached the disk.
- **Violated invariant:** a file that fails stops only itself, and a transcript never waits on the rewrite to reach the disk. Both are stated in the comment on `FileTranscriptionRun` (src/Deckle.App/FileTranscriptionRun.cs).
- **Recurrence cue:** a new step added to the run (a summary, a translation) that is awaited outside the per-file guard, or any change that moves the write after a slow step.
- **Guarded by:** `FileTranscriptionRunTests.ATranscriptionThatThrowsStopsOnlyItsFile`, `TheTranscriptIsOnDiskWhileTheRewriteRuns` and `AnEmptyOrThrowingRewriteKeepsTheRawTranscript`.
