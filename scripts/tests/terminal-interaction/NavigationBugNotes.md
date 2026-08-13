# Terminal-interaction navigation bug notes

## Horizontal movement selected a target on another row

- **Trigger:** Move right from the Release variant in the first wide Action Row.
- **Observed symptom:** Focus moved down to the Release variant of the next Action Row instead of right to Debug.
- **Cause:** An ineligible candidate used `continue` inside a nested PowerShell `switch`. That continued the `switch`, not the surrounding candidate loop, so the candidate was scored with its initial zero distance.
- **Violated invariant:** Left and Right consider only enabled targets on the current visual row; Up and Down consider only targets in the requested vertical direction.
- **Recurrence cue:** Directional candidate filtering happens inside another loop or `switch`, and rejected candidates can still reach the scoring block.

Regression coverage: `scripts/tests/terminal-interaction/navigation.tests.ps1` proves horizontal Action Variant movement, preferred-column vertical movement, narrow stacking, and keyboard access to paging controls.
