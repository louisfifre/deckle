# Profiles section bug notes

## Composed cards added to a profile's fold never showed

- **Trigger:** opening the Rewriting page with existing profiles, after the
  length threshold and shortcut cards were added to each profile's fold
  (v0.77.0).
- **Symptom (observed by the maintainer, then read through UI Automation on
  the running window):** an expanded profile fold held only its editor card;
  the "Use for long dictations", "From" and "Shortcut" cards were absent from
  the visual tree, and the diagnostics log held no exception.
- **Cause, in two layers:**
  1. `LlmProfilesSection.ComposeProfileCards` ran only when the expander's
     `DataContext` was a `ProfileViewModel`. The profiles' `ItemsRepeater`
     uses an `x:Bind` template, and the repeater drives such a template's
     bindings itself without setting the element's `DataContext`, so the test
     never matched and nothing was composed. This is inferred from the WinUI
     `ItemsRepeater` behaviour (DataContext is set only for templates without
     `x:Bind`) and fits every observation; it is confirmed once the cards show
     with the profile read from `Tag` instead.
  2. Had composition run, adding the cards to `SettingsExpander.Items` after
     the template applied would still have shown nothing: `Items` is a plain
     `List<object>` the expander hands to its inner repeater only when its
     template applies or when `Items` is assigned. The first fix (v0.77.1)
     addressed this layer alone, which is why it changed nothing on screen.
- **Violated invariants:** an element realized by an `ItemsRepeater` with an
  `x:Bind` template reads its item from a bound property (here `Tag`), never
  from `DataContext`; cards placed into a `SettingsExpander` after its
  template applied arrive as a new `Items` list.
- **Recurrence cue:** a `DataContext` read or a `DataContextChanged` handler on
  the root of an `x:Bind` DataTemplate inside an `ItemsRepeater`; any
  `expander.Items.Add` or `Remove` outside a constructor.
- **Since 2026-11-09:** the folds are gone; each profile is a card that opens
  its own page (`LlmProfilePage`), which composes the cards into a plain
  `StackPanel`. The second layer no longer applies; the first still does, and
  `ProfileCard_Click` reads the profile from `Tag` for that reason.
- **Guard:** none yet. The behaviour lives in WinUI controls inside a running
  window, which the unit suites do not host; it is checked on the Rewriting
  page, by eye or through UI Automation.
