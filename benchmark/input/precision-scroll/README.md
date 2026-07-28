# Precision-scroll capture analysis

This study compares three input chains without committing personal telemetry:

- physical two-finger trackpad gestures;
- physical mouse-wheel bursts;
- Deckle's injected Precision Touchpad gestures, which the existing trackpad recorder observes as a separate synthetic HID device.

Run from this directory:

```powershell
python analyze.py --output ..\..\..\artifacts\benchmark\input\precision-scroll\analysis.json
```

`--data-root` overrides the normal Deckle data root. `--trackpad-limit` and `--wheel-limit` restrict a quick iteration to the newest captures. `--burst-gap-ms` changes only offline wheel grouping; it does not tune production behavior. A truncated final row from an active recorder is counted and ignored. `--minimum-age-seconds 60` can exclude live files when a stable closed-session comparison matters more than the newest data.

Trackpad capture schema 2 assigns every device a session-local `dev` index and writes that index on every frame. The association comes directly from the Raw Input `hDevice` that emitted the frame, so simultaneous physical and synthetic streams remain separate even when their reports are interleaved. Legacy schema 1 files have no per-frame identity and can only be associated with the most recently declared device; that attribution is not reliable when several touchpads were present.

The report normalizes trackpad motion by each device's usable observed Y range when the recorded descriptor range does not contain the recorded coordinates. That fallback is necessary for legacy captures whose header lacks enough HID unit metadata for an honest millimetre conversion.

The analyzer treats a two-contact segment as a scrolling candidate only when its centroid path is predominantly vertical, directionally consistent, and keeps stable finger spacing. The classification deliberately remains kinematic: application outcome and user intent labels are not present in legacy captures.

Three kinematic profiles make that boundary visible in the report. `settled` ends below 20% of peak speed, `released_with_momentum` ends at or above 80%, and `controlled_release` lies between them. They are calibration envelopes, not claims about the user's intent; labelled replay fixtures remain the source of truth for behaviour tests.

Wheel bursts are grouped independently per recorded Raw Input device. Legacy hook-only sessions remain attributable only to `(mouse hook)`; new sessions retain the physical device path and VID/PID while the low-level hook remains limited to suppression. The report also separates raw versus hook sources and injected events.

The wheel report also measures what can be known causally from the first two or three inter-event gaps. It publishes cadence bands and precision/recall at candidate gap cutoffs against a documented proxy for sustained fast bursts. This evidence may shape a continuous transfer curve, but it is not an intent label and must not become a hard drag/flick classifier by itself.

Run the decoder tests with:

```powershell
python -m unittest test_capture_analysis.py
```
