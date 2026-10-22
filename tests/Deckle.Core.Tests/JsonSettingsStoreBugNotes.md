# Settings store bug notes

## A damaged settings file was overwritten by defaults with no copy kept

- **Trigger:** a settings file that does not parse at startup (a typo in a hand edit, a file cut short by a crash or a sync tool), followed by any save from the same store.
- **Observed symptom:** none observed. Found on 2026-10-22 by a code audit, then reproduced by a test: after the load fell back to defaults, the first save replaced the file and the person's text was gone. Only a Warning line named the fallback, and only for the stores wired to a log.
- **Cause:** `JsonSettingsStore.Load` returned `new T()` from its catch and left the file where it was. The next `Flush` serialized the defaults and moved them over it. Nothing copied the unreadable text first; the only backup, `SettingsBackupService`, runs from a button.
- **Resolution (2026-10-22):** the catch copies the file to `<path>.bad` before the defaults take over, replacing an earlier copy, and the Warning names the backup. A read that failed on I/O is not copied, since the file may be valid.
- **Violated invariant:** falling back to defaults must not destroy what the fallback replaces.
- **Recurrence cue:** a catch in a load path that returns defaults without keeping the input; a settings store whose warning says `corrupt_fallback` while no `.bad` file sits beside the path.
- **Regression coverage:** `JsonSettingsStoreTests.AnUnreadableFileKeepsItsTextInABackupBeforeDefaultsOverwriteIt` (regression) seeds a damaged file, flushes, and checks the backup holds the original text; `AReadableFileLeavesNoBackup` pins the other side.
