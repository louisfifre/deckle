# Terminal-interaction rendering bug notes

## Virtual-terminal drawing lost role colors and focus contrast

- **Trigger:** Draw a frame in a VT-capable Windows terminal with semantic foregrounds or a focused target.
- **Observed symptom:** Role foregrounds rendered like the host foreground, command keys and labels looked identical, and the focus background obscured its unchanged text instead of painting a contrasting foreground on the same cells.
- **Cause:** The renderer emitted VT cursor-position sequences but changed presentation attributes through the legacy `Console.ForegroundColor` and `Console.BackgroundColor` properties, so cursor, attributes, and text were not delivered as one ordered VT stream.
- **Violated invariant:** The Terminal Host paints every segment at its planned position with the resolved foreground and background, preserving both semantic role and interaction state.
- **Recurrence cue:** A VT drawing path mutates Console color properties, or coverage asserts only resolved theme values without asserting the terminal sequence that carries them.

Regression coverage: `scripts/tests/terminal-interaction/host.tests.ps1` asserts that VT segments serialize cursor position, SGR foreground and background, and text together, including focused Action contrast and distinct command-key and command-label tones.

## Focus highlight changed width with the selected label

- **Trigger:** Focus moved between targets whose labels had different lengths inside stable layout columns.
- **Observed symptom:** The highlighted area covered only the focus marker and label, so it appeared to change width while navigating and weakened the sense of moving through fixed columns.
- **Cause:** The renderer retained the target's full placement width but emitted a focused text segment only as long as its marker and label; the host therefore painted the focus background over fewer cells.
- **Violated invariant:** Interaction state overlays fill the complete stable target cell without changing its semantic layout width.
- **Recurrence cue:** A target segment with a background state is shorter than its corresponding frame placement, or a rendering test compares only trimmed target text.

Regression coverage: `scripts/tests/terminal-interaction/theme.tests.ps1` asserts that a focused target segment fills its complete grid-cell width while retaining its semantic presentation role and structural focus marker.

## Enabled navigation matched its disabled sibling

- **Trigger:** Render a paged footer with one available direction and one unavailable direction under a grayscale theme.
- **Observed symptom:** The enabled and disabled controls had the same tonal treatment, leaving the structural marker as their only visible distinction.
- **Cause:** The base `Navigation` role and the `Disabled` state overlay both resolved to `DarkGray`.
- **Violated invariant:** A state overlay remains distinguishable from the enabled treatment of its underlying presentation role; unavailable paging is structural and muted without making available navigation equally muted.
- **Recurrence cue:** An enabled selectable role maps to the same foreground and background as its Disabled overlay.

Regression coverage: `scripts/tests/terminal-interaction/theme.tests.ps1` keeps enabled `Navigation` at the middle grayscale level and disabled targets at the muted level while semantic hues remain confined to identity, consequence, and outcome roles.
