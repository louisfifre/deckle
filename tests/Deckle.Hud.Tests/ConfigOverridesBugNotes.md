# HUD config overrides bug notes

## One stored value of the wrong type made the whole HUD configuration throw

- **Trigger:** a HUD settings file holding a key the config still has, with a value of another type (a hand edit such as `"strokeThickness": "abc"`, or a field whose type changed in code after the file was written).
- **Observed symptom:** none observed. Found on 2026-10-22 by a code audit, then reproduced by a test: `ConfigOverrides.Apply` threw `JsonException`. The audit read the HUD call sites (`HudSettingsService`, the chrono readers) as having no try/catch; the Playground call sites were not opened, so the user-visible effect is inferred, not seen.
- **Cause:** `Apply` copied every known key into the defaults' JSON and deserialized the result once, so a single unreadable value failed the whole object. Only unknown keys were guarded, by the `ContainsKey` check.
- **Resolution (2026-10-22):** when the single read fails, `Apply` retries key by key and keeps only the overrides that still read. The bad field keeps its default; the other overrides apply. The next save rewrites the file without the bad key, since `Diff` compares the effective config with the defaults.
- **Violated invariant:** a damaged field costs that field its override and nothing more; stale content in the file never breaks the load.
- **Recurrence cue:** a read path that deserializes a user-editable object in one step with no per-field fallback; a new override type that is not a plain JSON value.
- **Regression coverage:** `HudSettingsOverridesTests.AValueOfTheWrongTypeIsIgnoredAndTheOtherOverridesStillApply` (regression).
