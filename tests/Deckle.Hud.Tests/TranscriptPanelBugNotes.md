# Transcript panel bug notes

## Long utterances did not wrap at the panel's right edge

- **Trigger:** dictate an utterance longer than one line of the transcript panel (text column 328 dip).
- **Observed symptom:** reported by the maintainer: the text did not wrap at the right edge. Not reproduced by an agent; nothing was run on screen.
- **Cause (inferred from Microsoft Learn, not observed):** the ScrollViewer had HorizontalScrollBarVisibility="Hidden". Per the ScrollBarVisibility documentation, Hidden leaves the content dimension unaffected by the ScrollViewer, while Disabled sets the content width to the ViewportWidth. The TextBlock therefore saw no width limit and TextWrapping="Wrap" had nothing to wrap against. HorizontalScrollMode="Disabled" only stops user panning and does not constrain layout. Render() measured the TextBlock at an explicit width, so the window height assumed wrapping that the live layout never performed.
- **Invariant:** the width the line count is measured at and the width the text is laid out at must be the same one.
- **Fix:** HorizontalScrollBarVisibility="Disabled" (vertical stays Hidden so the content keeps its natural height), plus an explicit TextBlock width set in Render() from the same constant the measurement uses.
- **Recurrence cue:** a TextBlock with TextWrapping inside a ScrollViewer whose scrolling axis is Hidden or Auto on the axis the text must wrap along; a window height computed from a measure the live layout does not reproduce.
- **Regression coverage:** none. The behavior lives in WinUI layout; there is no headless seam. The fix was not observed on screen: manual check required with an utterance of several lines.

## The "more above" fade dimmed every line instead of the first visible one

- **Trigger:** dictate enough text for the panel to overflow its viewport, so the first visible line should fade out. Reported in dark mode; the mechanism is theme-independent.
- **Observed symptom:** reported by the maintainer: the fade was applied to the whole text, every line dimmed. Reproduced on 2026-10-20 in a throwaway WinUI 3 app (Windows App SDK 1.8.260317003), not in Deckle itself.
- **Cause (observed in the probe app):** XAML maps a gradient `Foreground` of a `TextBlock` onto each text line, not onto the TextBlock's layout box. Both mapping modes behave this way. An `Absolute` ramp at y 80..100 on a 200-dip block left every line fully transparent (each line box is 20 dip tall, so every pixel of every line lies before the ramp start). A `RelativeToBoundingBox` ramp at 0.4..0.5 cut the top 40 % of every line. The panel's design placed one absolute ramp at the scroll offset in TextBlock coordinates, which no line could ever reach: with no overflow every line lay past the ramp end and rendered solid, with overflow every line lay before the ramp start and vanished, or dimmed as a whole when the ramp overlapped the line box.
- **Invariant:** a gradient on text can only vary within a line; anything that must vary across lines needs a clip, a second element or a composition mask.
- **Fix:** the faded text is a second `TextBlock` with the same text, width and line height, whose per-line ramp is now the feature rather than the bug. `ScrollToEnd` clips it to the first visible line and clips the solid copy to the lines below, both in TextBlock coordinates.
- **Recurrence cue:** a `LinearGradientBrush` on a `TextBlock.Foreground` whose stops are meant to land on specific lines or at a scroll offset; `MappingMode="Absolute"` on a text brush.
- **Regression coverage:** none. The behavior lives in XAML text rendering; there is no headless seam. The fixed layout was observed in the probe app only, not in Deckle's panel: manual check required with an overflowing take, in light and dark.
