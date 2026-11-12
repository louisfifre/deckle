# Three-finger drag recognizer bug notes

## A fourth finger during the grace window resumed the drag instead of ending it

- **Trigger:** a drag in progress, fingers lifted to fewer than three (grace window open), then a frame with four or more tips before the grace deadline.
- **Observed symptom:** none observed on hardware. Found on 2026-10-22 by a code audit, then reproduced by a test: the recognizer went back to Dragging on the four-tip frame and ended the drag with `fourth-finger` only on the next frame.
- **Cause:** the Grace branch resumed on `tips >= 3`, while the Dragging branch ends the drag on `tips > 3`. The class header says a fourth finger ends an active drag immediately, but Grace was not covered by that rule.
- **Resolution (2026-10-22):** Grace ends the drag with `fourth-finger` on more than three tips and resumes only on exactly three. The tip count recorded after that frame keeps the rising-edge rule in force, so returning to three fingers does not restart a drag.
- **Violated invariant:** four-finger gestures belong to Windows, so the button is released as soon as a fourth finger is seen, whatever the phase.
- **Recurrence cue:** a new phase or branch that compares the tip count with `>= 3` where the other phases split on `> 3`.
- **Regression coverage:** `ThreeFingerDragRecognizerTests.FourFingersDuringGraceEndTheDragInsteadOfResumingIt` (regression).

## The pick-up distance pressed before the fourth finger of a sweeping four-finger swipe

- **Trigger:** a four-finger swipe whose fingers land as three first, already moving: the three cover the pick-up distance on their first frame, and the fourth lands one frame later (about 11 ms on a Magic Trackpad).
- **Observed symptom:** the maintainer felt four-finger gestures as less clean since the pick-up delay and distance existed, and attributed it to the delay. Replaying the recorded frames showed the delay changes none of those gestures (same result at 0 and 30 ms); the distance (8 px) pressed the button before the fourth finger, which then released it as a stray click carried by the swipe. 35 such presses over 13 sessions. What Windows does with that press during its own gesture is inferred, not observed.
- **Cause:** `PressDue` let the distance press at any time after landing, while the delay is what was meant to wait for a fourth finger. A hand sweeping from the first frame got past the distance before the fourth finger could arrive.
- **Resolution (2026-11-12):** the distance counts only once `FourthFingerWindowMs` (20 ms, an engine constant) has passed since landing; a travel already past it presses when the window closes, by frame or by Tick. Replayed, the stray presses drop to 13 and a sweeping three-finger drag still presses on its second frame.
- **Violated invariant:** nothing presses the button while a fourth finger may still arrive; see the header bullet on the fourth-finger window in `ThreeFingerDragRecognizer.cs`.
- **Recurrence cue:** a new press guard, or a change to `PressDue`, that can fire inside the first frames after landing.
- **Regression coverage:** `ThreeFingerDragRecognizerTests.FourthFingerAfterASweepingLandingPressesNothing` (regression).

## A fourth finger the pad reports late left a stretched selection behind

- **Trigger:** four fingers set down gently, the fourth lightly: the pad reports three tips, then the fourth 68 to 113 ms later (no contact at all for it before, not even without tip), while the three have already swept 60 to 170 px.
- **Observed symptom:** the maintainer meant a four-finger gesture and got a three-finger drag that selected things, again and again (2026-11-12, Magic Trackpad, 30 ms delay, 8 px distance). The press, measured on the replayed frames, went down 22 ms after landing; the fourth finger then released the drag where it stood, keeping what it had stretched.
- **Cause:** the press cannot wait for a fourth finger that comes this late without making every drag late too (late fourth fingers span 44 to 191 ms over 34 sessions, so even 100 ms catches half), and the travel at 30 ms does not tell such a gesture from a real drag (medians 36 and 22 px, ranges overlap). The release then took the end point of a motion that was never a drag.
- **Resolution (2026-11-12):** a fourth finger within `LateFourthFingerWindowMs` (200 ms, engine constant) of landing withdraws the drag; the engine releases through `MouseInjector.ReleasePrimaryAtPress`, which moves the cursor back to the press point before the button comes up, so the gesture shrinks to a click at its start. A click remains: it is limited, not removed.
- **Violated invariant:** a four-finger gesture leaves nothing dragged behind; see the header bullet on the late fourth finger in `ThreeFingerDragRecognizer.cs`.
- **Recurrence cue:** a new way to end a drag on a fourth finger that releases in place, or a press point that `MouseInjector` stops recording.
- **Regression coverage:** `ThreeFingerDragRecognizerTests.LateFourthFingerAfterThePressWithdrawsTheDrag` (regression) guards the decision; the release at the press point goes through SendInput and has no test seam, so it is checked by hand.
