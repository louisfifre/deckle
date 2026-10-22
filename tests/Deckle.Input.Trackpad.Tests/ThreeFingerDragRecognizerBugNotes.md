# Three-finger drag recognizer bug notes

## A fourth finger during the grace window resumed the drag instead of ending it

- **Trigger:** a drag in progress, fingers lifted to fewer than three (grace window open), then a frame with four or more tips before the grace deadline.
- **Observed symptom:** none observed on hardware. Found on 2026-10-22 by a code audit, then reproduced by a test: the recognizer went back to Dragging on the four-tip frame and ended the drag with `fourth-finger` only on the next frame.
- **Cause:** the Grace branch resumed on `tips >= 3`, while the Dragging branch ends the drag on `tips > 3`. The class header says a fourth finger ends an active drag immediately, but Grace was not covered by that rule.
- **Resolution (2026-10-22):** Grace ends the drag with `fourth-finger` on more than three tips and resumes only on exactly three. The tip count recorded after that frame keeps the rising-edge rule in force, so returning to three fingers does not restart a drag.
- **Violated invariant:** four-finger gestures belong to Windows, so the button is released as soon as a fourth finger is seen, whatever the phase.
- **Recurrence cue:** a new phase or branch that compares the tip count with `>= 3` where the other phases split on `> 3`.
- **Regression coverage:** `ThreeFingerDragRecognizerTests.FourFingersDuringGraceEndTheDragInsteadOfResumingIt` (regression).
