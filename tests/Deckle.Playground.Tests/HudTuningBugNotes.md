# HUD tuning bug notes

## Removing collapse was not a valid fix for initial expansion motion

- **Trigger:** display a page with native sections initially expanded.
- **Observed symptom:** WinUI's initial state storyboard visibly opens every section. Replacing Expander with Border removed the user's ability to collapse sections.
- **Cause:** the correction removed an intentional interaction. GoToState(false) only skips transitions; the installed ExpandDown visual state carries its own 333 ms storyboard.
- **Invariant:** sections arrive already open, retain their native headers/chevrons and remain collapsible.
- **Fix:** restore native Expanders and finish only their current initial storyboard via the public SkipToFill API in Loaded. User-triggered animations remain unchanged. Saved expansion state is observed from IsExpanded immediately.
- **Recurrence cue:** replacing a control to avoid its motion, or treating VisualState.Storyboard as a VisualTransition.

Manual WinUI validation is still required for both visual issues: enter the page, collapse sections, replay/reset repeatedly, navigate away/back and switch targets. The first presented layout must be settled; click animations and saved expansion choices must remain. Compile-only validation cannot establish the first-frame rendering result; there is no natural headless seam for this control lifecycle.
