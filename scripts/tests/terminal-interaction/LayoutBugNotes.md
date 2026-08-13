# Terminal-interaction layout bug notes

## Quit shifted the regular option columns on its row

- **Trigger:** Render the wide root Action Menu where Setup shares a row with the trailing Quit command.
- **Observed symptom:** Setup moved left instead of remaining vertically aligned with Release above it.
- **Cause:** The renderer subtracted the trailing target width while calculating only the row that contained Quit, then recomputed that row's regular column widths.
- **Violated invariant:** An Action Menu calculates stable semantic tracks once per View; a trailing command occupies its own track without moving regular options.
- **Recurrence cue:** A row calculates its own option widths from its local target count or trailing target instead of consuming the View's shared grid.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves that Setup and Release share an exact horizontal coordinate while Quit remains farther right.

## Back occupied the label track

- **Trigger:** Open a nested Action Menu such as Project.
- **Observed symptom:** Back appeared at the left edge where Action Row subjects such as Docs and Version are placed.
- **Cause:** Back was positioned with a dedicated left-edge coordinate instead of using the Action Menu's first option track.
- **Violated invariant:** The visible Back Navigation Control is a selectable option with a stable position; it occupies the first option track and reserves the adjacent track for a future Rerun Action.
- **Recurrence cue:** A composition positions Back independently from the option grid used by other selectable targets.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves that Back and the first Project option share an exact horizontal coordinate.
