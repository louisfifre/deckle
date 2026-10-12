# Transcript panel bug notes

## Long utterances did not wrap at the panel's right edge

- **Trigger:** dictate an utterance longer than one line of the transcript panel (text column 328 dip).
- **Observed symptom:** reported by the maintainer: the text did not wrap at the right edge. Not reproduced by an agent; nothing was run on screen.
- **Cause (inferred from Microsoft Learn, not observed):** the ScrollViewer had HorizontalScrollBarVisibility="Hidden". Per the ScrollBarVisibility documentation, Hidden leaves the content dimension unaffected by the ScrollViewer, while Disabled sets the content width to the ViewportWidth. The TextBlock therefore saw no width limit and TextWrapping="Wrap" had nothing to wrap against. HorizontalScrollMode="Disabled" only stops user panning and does not constrain layout. Render() measured the TextBlock at an explicit width, so the window height assumed wrapping that the live layout never performed.
- **Invariant:** the width the line count is measured at and the width the text is laid out at must be the same one.
- **Fix:** HorizontalScrollBarVisibility="Disabled" (vertical stays Hidden so the content keeps its natural height), plus an explicit TextBlock width set in Render() from the same constant the measurement uses.
- **Recurrence cue:** a TextBlock with TextWrapping inside a ScrollViewer whose scrolling axis is Hidden or Auto on the axis the text must wrap along; a window height computed from a measure the live layout does not reproduce.
- **Regression coverage:** none. The behavior lives in WinUI layout; there is no headless seam. The fix was not observed on screen: manual check required with an utterance of several lines.
