# Settings backup bug notes

## A settings backup took the shell file and none of the module files

- **Trigger:** the user pressed Back up on the General page, then later Restore, with settings changed in a module (audio, HUD, LLM and so on).
- **Observed symptom:** the restore brought back the shell settings only; every module kept its current values. The card promised to carry the settings across machines, which a backup without the module files cannot do. Established by reading the code: the backup copied `AppSettings`' file and nothing under `modules/`.
- **Cause:** persistence was split into one `modules/<id>/settings.json` per module, but `SettingsBackupService` still assumed the single combined `settings.json` of the earlier layout.
- **Violated invariant:** a backup holds every settings file the app persists, and a restore puts them all back or none. Modules are found by looking at `modules/`, not from a list, so a new module is covered without editing the service.
- **Recurrence cue:** a new kind of settings file stored outside `modules/<id>/settings.json` (the personal dictionary, `personal-dictionary.json`, is one: it is learned data and is deliberately not backed up). Also, a module change younger than its 300 ms debounce is not on disk when the backup runs, because only the shell store can be flushed from `Deckle.Settings`.

Regression: `SettingsBackupRoundTripTests.ABackupCarriesEveryModuleSettingsFileAndRestoreBringsThemBack`.
