# Overlay card observability bug notes

## A card that could not be closed was invisible in the log

- Trigger: `HudOverlayWindow.ForceClose` throws while a card expires or while the manager is disposed.
- Symptom: nothing in the log. `CleanupFailed` was a Verbose event gated by the Windowing detail control, so with the defaults the failure left no line, while the manager went on and dropped the card from its list.
- Cause: the failure was classed as detail although the work went on degraded: the window may stay on screen with nothing tracking it any more.
- Violated invariant: a failure where work goes on degraded is a Warning that carries the step and the error on one line.
- Fix: `CleanupFailed` is a Warning with a readable message. Its id and payload are unchanged.
- Recurrence cue: an overlay card stuck on screen and no `Cannot close an overlay card` line.
- Guard: `OverlayCardCloseObservabilityTests.AnOverlayCardThatCannotBeClosedIsAWarningInTheLogWithItsCause`. It holds the event shape; that `HudOverlayManager` calls it from both close paths is read from the code (`CloseCard`), not tested, since the manager needs WinUI windows.
- Interpretation: whether a window that threw on `Close()` stays visible was not reproduced.