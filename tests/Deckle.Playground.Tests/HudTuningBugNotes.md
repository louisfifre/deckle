# HUD tuning bug notes

## Transport actions recreated the tuning controls

- **Trigger:** Replay pause or Resume speech in Utterance countdown; returning to the cached HUD page.
- **Observed symptom:** sections reopened and buttons moved during interaction.
- **Cause:** transport handlers rebuilt the entire panel to reset one number; Loaded rebuilt it despite page caching.
- **Invariant:** preview transport must preserve control identity, focus, expansion state and placement.
- **Fix:** retain the manual-position controls, build the cached page once, and preserve expansion choices across target/reset rebuilds.
- **Recurrence cue:** a transport handler reconstructs the tuning panel.

## Removing collapse was not a valid fix for initial expansion motion

- **Trigger:** display a page with native sections initially expanded.
- **Observed symptom:** WinUI's initial state storyboard visibly opens every section. Replacing Expander with Border removed the user's ability to collapse sections.
- **Cause:** the correction removed an intentional interaction. GoToState(false) only skips transitions; the installed ExpandDown visual state carries its own 333 ms storyboard.
- **Invariant:** sections arrive already open, retain their native headers/chevrons and remain collapsible.
- **Fix:** restore native Expanders and finish only their current initial storyboard via the public SkipToFill API in Loaded. User-triggered animations remain unchanged. Saved expansion state is observed from IsExpanded immediately.
- **Recurrence cue:** replacing a control to avoid its motion, or treating VisualState.Storyboard as a VisualTransition.

Manual WinUI validation is still required for both visual issues: enter the page, collapse sections, replay/reset repeatedly, navigate away/back and switch targets. The first presented layout must be settled; click animations and saved expansion choices must remain. Compile-only validation cannot establish the first-frame rendering result; there is no natural headless seam for this control lifecycle.

## Countdown duplicated material controls and omitted the material rotation

- **Trigger:** tune the full Transcribing material, then inspect the countdown.
- **Observed symptom:** a separate palette section, no full material inspection and a static material behind the countdown masks. Sweep mask also offered color variants that had no effect.
- **Cause:** the countdown repainted the palette but omitted its hue rotation; it owned an independent config projection. Mask/simulation/material controls were mixed.
- **Invariant:** one material source, independent mask geometry, and controls grouped by what they actually affect.
- **Fix:** share palette, hue rotation and Transcribing grading through HudMaterialTuning; expose countdown geometry/fade and preview-only inspection separately. Sweep mask has no color variant.
- **Regression coverage:** HudTargetControlsTests; shared-material isolation is covered by HudMaterialTuningTests in Deckle.Hud.Tests. Actual GPU rotation and inspection remain manual checks.

## Countdown onset was attenuated and could start outside the visible sides

- **Trigger:** begin a pause with feathered mirrored masks; choose a start angle outside the HUD's visible side range.
- **Observed symptom:** progress had started but the contour stayed invisible or appeared displaced.
- **Cause:** two fixed fade ramps were multiplied, suppressing the small initial overlap. The angle controls also permitted an invisible sweep before reaching the frame.
- **Invariant:** an opening grows from the chosen visible start, with the same height and timing on both sides.
- **Fix:** deform a single feathered canonical wedge per side, scale fades with its opening, and constrain both endpoint controls to the straight-side angle range. Oversize the canonical surface before its 45-degree rotation so source bounds cannot truncate the rays at the HUD edges.
- **Regression coverage:** UtteranceCountdownGeometryTests in Deckle.Hud.Tests covers fixed base, linear front, mirror and endpoint bounds. Alpha rasterization and first visible pixels require manual rendering validation.
