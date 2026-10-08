# HUD tuning sections bug notes

## Generic conic and hue rows were inert on the Recording target

- **Trigger:** In the Playground HUD tab, the Recording target was selected and a row of the generic "Conic fade & span" section (`ConicSpanTurns`, `ConicLeadFadeTurns`, `ConicTailFadeTurns`, `ConicFadeCurve`, `ArcMirror`) or the `HuePeriodSeconds` row of "Hue rotation" was moved.
- **Observed symptom:** The preview rebuilt and nothing changed. Established by reading the code; not replayed in the running app.
- **Cause:** `HudViewModel.ActiveTuningSections` listed `HudTuningSection.ConicFade` and `HudTuningSection.HueRotation` for Recording, while `HudComposition.CreateRecordingStroke` copies `RecordingConicSpanTurns`, `RecordingConicLeadFadeTurns`, `RecordingConicTailFadeTurns`, `RecordingConicFadeCurve`, `RecordingArcMirror` and `RecordingArcPhaseTurns` over the generic fields, freezes the arc, and replaces `HuePeriodSeconds` with `RecordingHuePeriodSeconds` (hue frozen when that is 0). Meanwhile `RecordingArcPhaseTurns` and `RecordingHuePeriodSeconds`, which do act on Recording, sat in the collapsed Parked expander.
- **Violated invariant:** A tuning section is shown for a target only when its rows reach that target's preview; when a target's own fields replace generic ones, the target's own section carries the rows.
- **Recurrence cue:** A stroke factory gains a per-variant override of a generic config field, or a target's section list is edited, without checking which section the rows that act live in.
- **Regression coverage:** `HudViewModelTests.RecordingTargetDoesNotShowTheGenericConicFadeSection` asserts that the Recording target's sections exclude `ConicFade` and `HueRotation` and include `Recording`.
