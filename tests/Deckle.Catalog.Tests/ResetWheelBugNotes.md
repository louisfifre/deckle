# Reset wheel bug notes

## Opening a settings page with a reset wheel overflowed the stack and killed the app

- **Trigger:** Navigating to any settings page whose composer builds a reset wheel (observed on Diagnostics, then on General, 2026-11-07, v0.87.14).
- **Observed symptom:** The navigation started and never completed; the process ended with exit code `0xC00000FD` (stack overflow), twice in a row, and the supervisor gave up relaunching. No managed exception, no log line: a stack overflow cannot be caught.
- **Cause:** Established by reading the code. Commit `3f94bec4` replaced every `ToolTipService.SetToolTip(reset, tooltip)` in the composer with a new helper `LabelResetButton(reset, tooltip)`, and the replacement also hit the helper's own first line, so `LabelResetButton` called itself unconditionally. The C# compiler gives no warning for unconditional self-recursion.
- **Violated invariant:** A page that composes renders. The helper sets the tooltip, then the accessible name (`SettingsComposer.Chrome.cs`, `LabelResetButton`).
- **Recurrence cue:** A mechanical find-and-replace that introduces a helper named after the call it replaces; any method whose first statement calls itself with the same arguments. The whole night's verification was compile and unit tests, and no test composes a page.
- **Regression coverage:** None. The Catalog suite cannot construct a WinUI element (no XAML runtime in the test host), so neither the helper nor a composed page can be exercised. The guard that would have caught it is opening the page in the running app, which the agents' rules forbid; it stays a manual check.
