# Crash relaunch policy bug notes

## A deliberate kill read as a crash

- **Trigger:** the supervised Deckle process ends through .NET's `Process.Kill`, `Stop-Process -Force` or a script built on them. The exit code is -1 (0xFFFFFFFF).
- **Observed symptom:** `IsCrash(-1)` was true, so the supervisor relaunched a process a person had just stopped and spent relaunch budget. The window is narrow (the supervisor sleeps, then checks whether the session is ending, and the same scripts usually kill the launcher first). Confirmed by reading the code; the -1 of Process.Kill is the runtime's behavior as known, not read from this repository.
- **Cause:** the test was the high bit alone, and 0xFFFFFFFF has it.
- **Violated invariant:** a deliberate end never counts as a crash.
- **Recurrence cue:** a `CrashRelaunched` row with code `0xFFFFFFFF` right after a manual stop.

Regression: `CrashRelaunchPolicyRegressionTests.An_exit_through_Process_Kill_is_not_a_crash`.
