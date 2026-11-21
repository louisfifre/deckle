# Trackpad observability bug notes

## The registry gesture change left no trace when it succeeded

- Trigger: the person turns the Windows three-finger gestures off (or back on) from the Trackpad page, and it works.
- Symptom: only failures reached the log. A neutralized or restored set of system gestures was an Informational line outside the log. After a support request the log could show that a write had failed but never that the machine's gesture settings had been changed.
- Cause: the success lines were written as plain Informational events, while their failures were Warnings (which travel on their own).
- Violated invariant: work that changes the machine writes its start and its end in the log.
- Fix: `GesturesNeutralized` and `GesturesRestored` carry the Milestone tag. (The connection repair script that shared this fix was removed on 2026-11-21.)
- Recurrence cue: a `Gesture registry write failed` line with no earlier `neutralized` line to say what it followed.
- Guard: `DeckleTrackpadSourceTests.ASystemSettingChangeReachesTheLogOnSuccessToo`.

## A failed button release hid behind the first injection failure

- Trigger: the synthetic mouse button cannot be released at the end of a three-finger drag, typically because an elevated window refuses `SendInput`.
- Symptom: when an earlier press or move had already opened the injection incident, the failed release was not logged at all, although it is the failure that can leave the button logically held after the drag.
- Cause: all injection failures shared one incident, which writes only the first failing action.
- Violated invariant: a failure carries its cause on one line; an incident's opener does not stand for every failure inside it when one of them has a consequence of its own.
- Fix: `PrimaryReleaseFailed(win32_error)` is a Warning written for each failed release, in addition to the incident logic.
- Recurrence cue: an `injection incident` opened on `press` or `move` with the button stuck down afterwards and no release line.
- Guard: `DeckleTrackpadSourceTests.AFailedButtonReleaseIsAWarningWithItsWin32Error` holds the shape of the event. That the engine writes it at the failed release is read from the code, not tested: the injector is created inside the engine and has no seam to make `SendInput` fail.
- Uncertain: whether Windows really keeps the button held after a refused release was not reproduced; the Warning only makes the case visible.