# Terminal-interaction host bug notes

## Arrow commands rendered as a question mark

- **Trigger:** Open the interaction preview in Windows PowerShell 5.1 while the console output encoding uses the inherited OEM code page.
- **Observed symptom:** The four-arrow command key in the Persistent Header renders as `? Move`, although box-drawing separators remain visible.
- **Cause:** The OEM code page supports the separator glyph but not the Unicode arrow glyphs; `Console.Write` replaces the unrepresentable arrows with a question mark.
- **Violated invariant:** A global command indication preserves its key or gesture across supported engines and terminal hosts, or renders an explicit text fallback.
- **Recurrence cue:** A terminal session emits non-ASCII interface glyphs without selecting and later restoring a compatible console output encoding.

Regression coverage: `scripts/tests/terminal-interaction/layout.tests.ps1` proves the Unicode and ASCII render plans, while the hidden-console smoke test proves UTF-8 activation and exact output-encoding restoration under Windows PowerShell 5.1 and PowerShell 7.

## Ellipsis interpolation consumed the Access label

- **Trigger:** Import the UTF-8-without-BOM preview catalog in Windows PowerShell 5.1 after writing an Access label as `"$Label…"`.
- **Observed symptom:** `Project…` became only `€¦`; the `Project` portion disappeared before layout.
- **Cause:** Windows PowerShell 5.1 decoded the UTF-8 ellipsis bytes through its legacy source encoding. The first decoded character extended the interpolated variable name from `$Label`, so the undefined longer variable expanded to nothing and only the remaining decoded characters survived.
- **Violated invariant:** Shared interface descriptors produce the same labels under Windows PowerShell 5.1 and PowerShell 7.
- **Recurrence cue:** A non-ASCII character immediately follows an unbraced interpolated variable in a cross-engine PowerShell source file.

Regression coverage: `scripts/tests/terminal-interaction/theme.tests.ps1` verifies under both engines that every preview Access retains its complete label and ends with the Unicode ellipsis assembled from its code point.
