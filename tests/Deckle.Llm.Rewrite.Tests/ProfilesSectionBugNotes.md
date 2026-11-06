# Profiles section bug notes

## Composed cards added to a profile's fold never showed

- **Trigger:** opening the Rewriting page with existing profiles, after the
  length threshold and shortcut cards were added to each profile's fold
  (v0.77.0).
- **Symptom (observed by the maintainer):** the fold of an existing profile
  showed only its editor card; the "Use for long dictations", "From" and
  "Shortcut" cards were missing. A freshly added profile behaved differently,
  not reliably reproduced.
- **Cause (established from the Community Toolkit source):**
  `SettingsExpander.Items` is a plain `List<object>`. The expander hands it to
  its inner `ItemsRepeater` only when its template applies or when the `Items`
  property is assigned a new list. `LlmProfilesSection.ComposeProfileCards`
  composed the cards on the expander's `Loaded`, after the template applied,
  and added them to the existing list, which the repeater never saw. Why a new
  profile could differ is inferred, not verified: its expander may load before
  its template applies, so the repeater then reads the already filled list.
- **Violated invariant:** cards placed into a `SettingsExpander` after its
  template applied must arrive as a new `Items` list, never by mutating the
  current one.
- **Recurrence cue:** any code that calls `expander.Items.Add` or `Remove`
  outside a constructor. The General page's backup fold inserts into `Items`
  in its page constructor, before any template, which is why it works.
- **Guard:** none yet. The behavior lives in a WinUI control inside a running
  window, which the unit suites do not host; it is checked by eye on the
  Rewriting page.
