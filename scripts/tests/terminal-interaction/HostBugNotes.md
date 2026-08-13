# Terminal-interaction host bug notes

## Arrow commands rendered as a question mark

- **Trigger:** Open the interaction preview in Windows PowerShell 5.1 while the console output encoding uses the inherited OEM code page.
- **Observed symptom:** The four-arrow command key in the Persistent Header renders as `? Move`, although box-drawing separators remain visible.
- **Cause:** The OEM code page supports the separator glyph but not the Unicode arrow glyphs; `Console.Write` replaces the unrepresentable arrows with a question mark.
- **Violated invariant:** A global command indication preserves its key or gesture across supported engines and terminal hosts, or renders an explicit text fallback.
- **Recurrence cue:** A terminal session emits non-ASCII interface glyphs without selecting and later restoring a compatible console output encoding.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves the Unicode and ASCII render plans, while the hidden-console smoke test proves UTF-8 activation and exact output-encoding restoration under Windows PowerShell 5.1 and PowerShell 7.
