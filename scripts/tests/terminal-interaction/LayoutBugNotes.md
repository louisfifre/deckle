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

## Back lost the grid of its owning Action Menu

- **Trigger:** Open a completed Execution from the root Release Action at a wide terminal width after viewing a nested Project Action Menu.
- **Observed symptom:** Back aligns with README pulse in Project but moves two cells left in the root-owned Execution.
- **Cause:** The child composition recalculated Back from a generic menu grid whose minimum label width was shorter than the owning root Action Menu's `Build (no run)` label track.
- **Violated invariant:** A child View inherits the first option track and cell width of its owning Action Menu; its body composition does not invent another navigation grid.
- **Recurrence cue:** Preparation, Content, or Execution renders without resolving its `OwnerActionMenuId` to the retained Action Menu descriptor.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves that root-owned Execution and Maintenance-owned Preparation inherit the exact first option placement of their respective Action Menus; `flows.tests.ps1` proves the interaction core resolves the retained owner from the View stack.

## Paging footer escaped its content region

- **Trigger:** Render a completed wide Execution whose Journal has more than one page.
- **Observed symptom:** Previous and Next appeared under the Journal while the Wheel indication appeared under Tracking, with no boundary above them; an unavailable direction also used the same `x` marker as an error.
- **Cause:** The shared paging footer always consumed the complete frame width and delegated its unavailable presentation to the generic target marker.
- **Violated invariant:** Paging controls and Scrolling Command Indications form one locally framed footer owned by the content they page; a natural page boundary is neutral rather than erroneous.
- **Recurrence cue:** Footer geometry derives from the complete View instead of its paged region, or a page boundary renders through the generic disabled marker.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves the paging separator, neutral boundary marker, and wide Journal footer bounds.
