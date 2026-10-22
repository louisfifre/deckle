# Loc mirror bug notes

## Setup wizard strings missing from the app's resource map

- **Trigger:** The setup wizard rework added code keys to `Deckle.Setup`'s `.resw` (the step rail labels, the module cost line, the "Requires" line) and reworded three step headers there, without touching the `Deckle.App` copy.
- **Observed symptom:** Not observed at runtime. The gap was found by comparing the two files before merge. By `Loc.Get`'s contract, a Debug build would show `[!Setup_Step_Modules_Label]`-style markers in the step rail and on each module card, a Release build empty labels, and the three headers their old wording.
- **Cause:** `Loc.Get` resolves through the default `ResourceLoader`, which binds to the app's root `Resources` map only. A module's own entries are reachable through `x:Uid` or `Loc.GetFrom`, never through `Loc.Get`. Nothing in the build checks that the mirror exists.
- **Violated invariant:** Every dotless key in `Deckle.Setup`'s `.resw` has an entry with the same value in `Deckle.App`'s `.resw`, the module copy being the source of truth for wording.
- **Recurrence cue:** A setup change adds or rewords a key read through `Loc.Get` or `Loc.Format` in `src/Deckle.Setup/Strings`, and the diff leaves `src/Deckle.App/Strings` untouched.
- **Regression coverage:** `SetupStringMirrorTests.Every_setup_code_key_has_an_identical_app_mirror` compares the two files key by key. Other modules that mirror keys the same way are not covered yet: some read their own map through `Loc.GetFrom`, so a rule over every module would need them sorted first.
