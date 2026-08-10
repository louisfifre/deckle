# Running-process bug notes

## Uninstall could not own a surviving legacy provider

- **Trigger:** Uninstall runs after an upgraded Deckle adopted the still-running Anytype provider from `%LOCALAPPDATA%\Programs\Deckle\anytype`, either from the default app layout or from a custom Deckle install.
- **Observed symptom:** In the default layout the recursive app gate reported the headless provider as “Deckle is still running” and returned before provider teardown. With a custom app install the legacy root sat outside the scanned folder, so uninstall could report success while leaving that process and payload behind.
- **Cause:** The app running-process gate did not exclude the provider it was supposed to stop, while teardown owned only the new external provider root and omitted the legacy executable root.
- **Violated invariant:** Explicit uninstall owns every Deckle provider executable root independently of the optional data choice. It first proves that the resident app is absent, then stops exact provider images, and reports any residual instead of claiming completion.
- **Recurrence cue:** A provider layout is added or migrated without appearing in `InstallPaths.ProviderDirectories`, or uninstall scans a provider root as part of the resident-app gate.
- **Regression coverage:** `InstallPathsTests.Provider_ownership_includes_external_and_legacy_executable_roots` pins both uninstall roots; the uninstaller consumes that same collection and excludes the legacy root from the resident-app gate.
