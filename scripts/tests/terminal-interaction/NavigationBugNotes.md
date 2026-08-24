# Terminal-interaction navigation bug notes

## Horizontal movement selected a target on another row

- **Trigger:** Move right from the Release variant in the first wide Action Row.
- **Observed symptom:** Focus moved down to the Release variant of the next Action Row instead of right to Debug.
- **Cause:** An ineligible candidate used `continue` inside a nested PowerShell `switch`. That continued the `switch`, not the surrounding candidate loop, so the candidate was scored with its initial zero distance.
- **Violated invariant:** Left and Right consider only enabled targets on the current visual row; Up and Down consider only targets in the requested vertical direction.
- **Recurrence cue:** Directional candidate filtering happens inside another loop or `switch`, and rejected candidates can still reach the scoring block.

Regression coverage: `scripts/tests/terminal-interaction/navigation.tests.ps1` proves horizontal Action Variant movement, preferred-column vertical movement, narrow stacking, and keyboard access to paging controls.

## Paging returned focus to Back at a boundary

- **Trigger:** Activate Next or Previous when the resulting page makes that same paging control unavailable.
- **Observed symptom:** Focus leaves the pagination footer and returns to Back instead of remaining with the available paging control.
- **Cause:** Paging retains the activated target identifier after changing the offset. On the next render that target is disabled, so generic focus recovery discards it and selects the first enabled target in visual order, which is Back.
- **Violated invariant:** Pagination retains the user's interaction locus; when the activated direction reaches a boundary, focus transfers to the available sibling control in the same footer.
- **Recurrence cue:** A state transition can disable or remove the focused paging target and relies on generic initial-focus recovery instead of an explicit local focus transition.

Regression coverage: `scripts/tests/terminal-interaction/navigation.tests.ps1` proves focus transfer at the first and latest page boundaries and focus retention while another page remains.
