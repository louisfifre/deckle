# Terminal-interaction rendering bug notes

## Focus highlight changed width with the selected label

- **Trigger:** Focus moved between targets whose labels had different lengths inside stable layout columns.
- **Observed symptom:** The highlighted area covered only the focus marker and label, so it appeared to change width while navigating and weakened the sense of moving through fixed columns.
- **Cause:** The renderer retained the target's full placement width but emitted a focused text segment only as long as its marker and label; the host therefore painted the focus background over fewer cells.
- **Violated invariant:** Interaction state overlays fill the complete stable target cell without changing its semantic layout width.
- **Recurrence cue:** A target segment with a background state is shorter than its corresponding frame placement, or a rendering test compares only trimmed target text.

Regression coverage: `scripts/tests/terminal-interaction/theme.tests.ps1` asserts that a focused target segment fills its complete grid-cell width while retaining its semantic presentation role and structural focus marker.
