# Precision scroll bug notes

## A worker that ended on its own left the wheel native for the whole session

- Trigger: the precision-scroll worker ends by itself, because the desktop refused a synthetic touchpad frame (UAC prompt, lock screen, a full-screen app taking input), because a burst of wheel ticks filled the queue, or because the frame builder threw.
- Symptom: the wheel scrolls natively although the switch in Settings is on. Nothing started precision scrolling again, and the app did not say it was off.
- Cause: two defects together. `App.PrecisionScroll.cs` kept `_precisionScrollRunning` true after the worker died, so a reconcile saw nothing to start; and `PrecisionScrollEngine.Start` returned true whenever `_worker` was set, which only `Stop` clears, so even a direct `Start()` reported a dead worker as running.
- Violated invariant: a switch that is on means the feature runs, or the person is told it does not and why.
- Fix: the engine raises `EndedOnItsOwn` and marks `_workerEnded`, so `Start` reaps a dead worker and starts for real. `PrecisionScrollRuntime` owns the engine and its share of the input host: when the worker ends on its own it restarts once after `RestartDelay`; if that run ends again before `StableRun`, or the restart cannot start, the wheel stays native and one Warning (`FellBackToNativeWheel`) says why. Only the person toggling the switch closes that incident, with a Journal-tagged `FallbackRecovered`. The app no longer keeps a running flag of its own.
- Recurrence cue: an `Precision scrolling disabled | outcome=failed` line followed by no `Precision scrolling enabled` and no `stays off` Warning.
- Guard: `PrecisionScrollRuntimeTests`, which replace the engine, the host, the delay and the clock with fakes.
- Interpretation: `RestartDelay` (5 s) and `StableRun` (5 min) are judgments, see the comments on them. The `_workerEnded` change in the real engine is not covered by a test: `PrecisionTouchpadInjector.TryCreate` creates a synthetic touchpad device, which a test must not leave on the machine. It is covered only through the runtime's fake.
