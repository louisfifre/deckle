# Log window scroll bug notes

## Scrolling to the end stopped rows short of the last entry

- **Trigger:** a log window whose rows exceed its height and mix one-line rows with two-line Warning or Error rows, then a jump to the end (auto-scroll, the Show detail toggle, the self-check's "end" image). Reproduced 2026-11-13 on the self-check image `log-detail-end.png`.
- **Observed symptom:** the view stopped several rows before the last entry, the bottom row cut by the window's edge; the rows after it, two-line Warnings among them, were never shown.
- **Cause (established by measurement):** `LogWindow.ScrollToBottom` read `ScrollViewer.ScrollableHeight` right after `UpdateLayout`. The `ListView` is virtualized and estimates the height of the rows it has not built from those it has; two-line rows make that estimate run short, so the offset aimed at was short of the real end. With `ScrollIntoView` on the last entry first, a probe read offset = ScrollableHeight = end before `ChangeView`.
- **Violated invariant:** a jump to the end shows the last entry whole, with the footer below it. The trap is commented at the line, in `LogWindow.ScrollToBottom` (`src/Deckle.App/LogWindow.Model.cs`).
- **Recurrence cue:** an "end" image of the log window whose last row is cut, or whose last timestamp is older than lines the run is known to have written.

A second cut on the same image was the self-check's own: the window is never activated there, so auto-scroll does not follow the rows written after the jump, and they landed under the view. The self-check now jumps again just before the image (`App.SelfCheck.cs`). A person's window, activated, follows them; this is inferred from the code, not observed on screen.

Regression: no automated test, because the estimate exists only in a laid-out WinUI `ListView` and no suite hosts one. The self-check image `log-detail-end.png` guards it: its last row must be the last entry, whole.
