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
- Superseded 2026-10-12: HudMaterialTuning and its tests are deleted. The countdown preview takes its material from the HUD tab's own TuningModel; the live countdown uses the compiled defaults.

## Countdown onset was attenuated and could start outside the visible sides

- **Trigger:** begin a pause with feathered mirrored masks; choose a start angle outside the HUD's visible side range.
- **Observed symptom:** progress had started but the contour stayed invisible or appeared displaced.
- **Cause:** two fixed fade ramps were multiplied, suppressing the small initial overlap. The angle controls also permitted an invisible sweep before reaching the frame.
- **Invariant:** an opening grows from the chosen visible start, with the same height and timing on both sides.
- **Fix:** deform a single feathered canonical wedge per side, scale fades with its opening, and constrain both endpoint controls to the frame-corner angle range. Oversize the canonical surface before its 45-degree rotation so source bounds cannot truncate the rays at the HUD edges.
- **Regression coverage:** UtteranceCountdownGeometryTests in Deckle.Hud.Tests covers fixed base, linear front, mirror and endpoint bounds. Alpha rasterization and first visible pixels require manual rendering validation.
- Superseded 2026-10-08: the countdown now grows from the top and bottom edge centres toward the corners (reach fractions); the invariant is carried by UtteranceCountdownGeometryTests.FullReachLightsTheFrameCorners.

## Angle controls could not reach the frame corners

- **Trigger:** increase Start angle or decrease End angle to reach a HUD corner.
- **Observed symptom:** controls stopped at +12/-12 degrees for a 272x78 frame.
- **Cause:** the bound subtracted the rounded corner radius before calculating the angle, then the UI floored that result.
- **Invariant:** both controls reach the center-to-corner ray for the current frame aspect ratio without an arbitrary whole-degree cap.
- **Fix:** derive the bound from the whole half-height and half-width; retain fractional bounds and expose 0.1-degree steps.
- **Regression coverage:** UtteranceCountdownGeometryTests.EndpointLimitReachesFrameCornersForDifferentHudSizes. Corner pixels and fades remain a manual rendering check.
- Superseded 2026-10-08: the countdown now grows from the top and bottom edge centres toward the corners (reach fractions); the invariant is carried by UtteranceCountdownGeometryTests.FullReachLightsTheFrameCorners.

## Base stroke thickness was missing from live geometry tuning

- **Trigger:** try to reduce the global Recording/Transcribing/Rewriting stroke width from HUD geometry.
- **Observed symptom:** only InsetDip was available; countdown thickness did not change the base stroke.
- **Cause:** the base stroke width remained a private Composition constant outside the tunable config.
- **Invariant:** the base stroke has one shared, live width; material, sweep masks and countdown geometry remain independent.
- **Fix:** carry StrokeThickness and InsetDip through the production config and a process-local geometry source observed by the real HUD; both update together rather than relying on a mutable inset read only when the next stroke is created.
- **Regression coverage:** HudGeometryTuningTests.SharedThicknessPreservesMaterialMasksAndCountdownGeometry. Visual thickness changes require a manual HUD check.
- 2026-10-08: the countdown contour now reads the same thickness and inset as the base stroke; its own thickness knob is gone.
- Superseded 2026-10-12: HudGeometryTuning and its tests are deleted. StrokeThickness and InsetDip stay in the production config; the live HUD reads their compiled defaults, the Playground tunes them in its previews only.

## The HUD tab wrote its tuning into the live HUD

- **Trigger:** open the HUD tab, or move any knob there (after the 300 ms debounce), press Play/Stop, switch target, or close the Playground.
- **Observed symptom:** the live HUD redrew with Playground values during dictation.
- **Cause:** the HUD tab published its model to process-wide static sources that the live HUD reads and watches. `TuningModel.PublishLive` and `HudPage.PublishCountdownTuning` ran in `ApplyTarget` (every rebuild, including the first page load) and in `DisposeResources`; `TuningModel.LoadLive` and `LoadCountdownTuning` loaded those sources back when the tab opened. The sources were `HudMaterialTuning`, `HudGeometryTuning`, `HudStrokeTuning` and `UtteranceCountdownTuning`. Every `HudChrono` rebuilt from them on `Changed`, the live instance included.
- **Invariant:** the Playground never writes what the live HUD reads. A value found there becomes a default by editing the compiled defaults.
- **Fix:** the publication and reload calls are gone; the tab opens on the compiled defaults and every preview builds from the page's own model. The four static sources had no other writer and are deleted, so the live HUD builds from the compiled defaults only.
- **Regression coverage:** TuningModelTests.AnEditChangesItsOwnPreviewOnlyAndAFreshTabOpensOnTheShippingDefaults and TuningModelTests.TheLiveHudOffersNoProcessWideEntryForAHudConfig. That the live HUD stays unchanged while the tab is used remains a manual check.
- **Recurrence cue:** any static tuning source with a setter reachable from Deckle.Playground. `AudioLevelMapper` (Deckle.Audio) was one: the Audio mapping knobs and Reset all wrote statics the live HUD reads on each audio sample.
- 2026-10-12, later the same day: Louis applied the rule to `AudioLevelMapper` too ("la même règle pour tous, le moins de spécificités"). The Audio mapping section and the Reset all writes are gone; its three window values are Settings > Recording's, EmaAlpha stays a constant, and the Recording preview maps the simulated RMS through the live calibration. TuningModelTests.ThePlaygroundTouchesNoAudioType pins it: the Playground assembly references nothing from Deckle.Audio.
- 2026-10-14: Louis asked for the opposite gesture, explicit this time: the tab opens on the values the live HUD uses and a floating bar saves what the knobs show, writing only what moved. The invariant narrows to: the Playground never writes what the live HUD reads *on its own*; the one path is `HudSettingsService.Save`, behind the bar's Save button. The live HUD builds its stroke and countdown from `HudSettingsService.Effective*()` (stored overrides over the compiled defaults) and rebuilds on `Changed`. The file holds only the fields that differ from the compiled defaults, so a field moved back to its default leaves it; pinned by HudSettingsOverridesTests in Deckle.Hud.Tests, and TuningModelTests.LoadCopiesEveryField keeps the tab's load complete. That the bar appears on a knob move and the live HUD follows a Save stay manual checks.
