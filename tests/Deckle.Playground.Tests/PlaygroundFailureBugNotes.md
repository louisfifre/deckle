# Playground failure observability bug notes

## The Playground's failures said that something failed and not what

- Trigger: a page navigation is rejected or fails, a Hue bridge call from the Ambient tab throws, or the screen capture does not start.
- Symptom: the Warning or Error in the log read "A navigation request was rejected", "Navigating to a page failed", "A Hue bridge call failed" and "Starting the screen capture failed", with no payload. The page, the operation and the error type were in a Verbose detail event that the default log does not keep, so a person reading the log alone could not tell which page or which call.
- Cause: each failure was split into a bare marker and a Verbose detail, and the marker carried nothing.
- Violated invariant: a failure carries its cause, the step and the error, on one line.
- Fix: `NavigationRejected(reason)`, `NavigationFailed(page, reason)`, `HueCallFailed(op, ex_type)` and `ScreenCaptureStartFailed(ex_type)` carry their cause. The page is the page type's name, the operation a fixed word of the code, the error only its type. The Verbose details stay, with the exception message that the log does not keep.
- Recurrence cue: a Playground Warning or Error in the log whose text has no field after the sentence.
- Guard: `PlaygroundFailureObservabilityTests` (one test per event, and a check that every payload of the provider appears in its template). The callers in the windows and pages are not exercised, since they need a WinUI window; that the callers pass the right values is read from the code, not tested.