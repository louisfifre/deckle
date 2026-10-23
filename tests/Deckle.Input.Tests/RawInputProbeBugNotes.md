# Raw input probe bug notes

## A device listing that failed read as "no touchpad"

- Trigger: `GetRawInputDeviceList` fails at boot, in its count call or in its list call.
- Symptom: the probe logged `TouchpadAbsent` ("No precision touchpad detected") and the host start line said `touchpads=0`, exactly as on a machine that has none. A failed listing and an empty machine could not be told apart, and the person was left without gestures for a reason the log did not carry.
- Cause: the count call's failure and its legitimate `count == 0` answer shared one condition, and the list call's failure took the same branch.
- Violated invariant: a failure carries its cause, the step and the error, on one line; work not done says so with its reason.
- Fix: `TouchpadEnumerationFailed(step, win32_error)` is a Warning written for each failing call, with the last Win32 error. `TouchpadAbsent` is now only written when the list was read and held no touchpad.
- Recurrence cue: `touchpads=0` in the host start line with a Win32 error nowhere in the log on a laptop that has a touchpad.
- Guard: `DeckleInputSourceTests.AFailedDeviceListingIsAWarningNotANoTouchpadLine` holds the shape of the event. That the probe calls it at the two failing branches is read from the code, not tested: `GetRawInputDeviceList` is a direct native call and the probe has no seam to make it fail.
- Not changed: the audit also named uncounted report-cadence drops in the same file; that part was not examined here.