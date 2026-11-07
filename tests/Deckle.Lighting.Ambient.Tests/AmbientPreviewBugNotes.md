# Ambient preview bug notes

## A stuck scan release closed the scan gate and the next Ambient Light start hung silently

- **Trigger:** The Settings preview scan ran (pane open, Ambient Light off), then ended while a capture frame was still inside the sampler; Ambient Light was then turned on.
- **Observed symptom:** In the log of 2026-11-07 the scan starts at 13:42:02, the capture stops 99 s later with `CaptureLoopStopTimedOut` (the loop did not exit in 2 s) and no `PreviewScanStopped` is written; at 13:45:50 `PipelineStarting` is followed by no `PipelineStarted` and no failure. The person saw the scan vanish and no message. Established from the log.
- **Cause:** `EndPreviewScanAsync` holds `_scanGate` while it releases the capture and the sampler. The sampler's `DisposeAsync` takes the sampler lock, which a frame stuck in the capture loop still holds, so the release never returned and the gate stayed closed; `StartAsync` awaits that gate before it builds its capture. That a frame was stuck inside the sampler is inferred from the timed-out loop; what blocked the frame is not established.
- **Violated invariant:** No wait on a release of the display is unbounded; a release that overruns is logged (`PreviewScanStopped`, outcome `timed_out`) and abandoned.
- **Recurrence cue:** An `await` of a capture or sampler release under `_scanGate` (or any gate a start waits on) with no time limit; a `PreviewScanStarting` with no `PreviewScanStopped` in the log.
- **Regression coverage:** `AmbientPreviewRobustnessTests.AReleaseThatNeverEndsIsAbandonedAfterTheLimit` drives `AmbientScanRelease.RunBoundedAsync` with a task that never ends. The engine's own use of it needs a live capture and is covered by reading only.

## The preview drew the bands of lights the setup does not have

- **Trigger:** Zones mode on, Ambient Light off, pane open, with lights on some edges only (top, left and right; none below).
- **Observed symptom:** All four bands were drawn. Reported on screen, with "a line in the middle"; the line is not reproduced (see the task report).
- **Cause:** `ApplyZone` drew every band neutral when no run was active; only a run looked at which light follows which zone.
- **Violated invariant:** The preview draws the bands the lights are fed from, no others.
- **Recurrence cue:** A zone band shown without reading `AmbientSettings.LightZones`.
- **Regression coverage:** `AmbientPreviewRobustnessTests` (`OnlyTheZonesSomeLightFollowsAreDrawn`, `ALightSetToNoZoneDrawsNoBand`, `ALightOutsideTheRunDrawsNoBand`) on `AmbientPreviewZones.Used`. The drawing itself is covered by reading only.

## The room picker lost the chosen room

- **Trigger:** Opening the Ambient page, or pressing Load rooms, with a room already chosen.
- **Observed symptom:** The picker was empty after opening the page; after Load rooms it could land on another room. Reported as "the rooms reset".
- **Cause:** The page never read the list when it opened. `LoadHueGroupsAsync` lifted its event suppression before it set `SelectedIndex`, and selected the first room when the stored id was not listed; the change handler then saved that first room over `HueLastGroupId`. No earlier commit covers this (826a3b33 handles an unreachable bridge only).
- **Violated invariant:** Showing the stored choice never writes a choice.
- **Recurrence cue:** A `SelectedIndex` assignment after the suppression flag is lowered; a fallback to index 0.
- **Regression coverage:** `AmbientPreviewRobustnessTests.NoStoredRoomOrOneTheBridgeDoesNotListSelectsNothing` and `TheStoredRoomIsTheOneSelected` on `HueGroupSelection.IndexOf`. The page wiring is covered by reading only.

## A grey preview said nothing

- **Trigger:** The scan failed to start, ended on its own, or a capture delivered no frame.
- **Observed symptom:** A grey stage and no message.
- **Cause:** A held scan only kept the display's outline; nothing watched the frame count.
- **Violated invariant:** Every failure of the picture is said on the page.
- **Recurrence cue:** A branch that sets `_scanHeld` or leaves the stage grey without opening the problem bar.
- **Regression coverage:** `AmbientPreviewHealth.Classify` tests in `AmbientPreviewRobustnessTests`. Why the capture sent no frame while the Playground was open is not established.
