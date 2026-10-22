# Settings store bug notes

## A damaged settings file was overwritten by defaults with no copy kept

- **Trigger:** a settings file that does not parse at startup (a typo in a hand edit, a file cut short by a crash or a sync tool), followed by any save from the same store.
- **Observed symptom:** none observed. Found on 2026-10-22 by a code audit, then reproduced by a test: after the load fell back to defaults, the first save replaced the file and the person's text was gone. Only a Warning line named the fallback, and only for the stores wired to a log.
- **Cause:** `JsonSettingsStore.Load` returned `new T()` from its catch and left the file where it was. The next `Flush` serialized the defaults and moved them over it. Nothing copied the unreadable text first; the only backup, `SettingsBackupService`, runs from a button.
- **Resolution (2026-10-22):** the catch copies the file to `<path>.bad` before the defaults take over, replacing an earlier copy, and the Warning names the backup. A read that failed on I/O is not copied, since the file may be valid.
- **Violated invariant:** falling back to defaults must not destroy what the fallback replaces.
- **Recurrence cue:** a catch in a load path that returns defaults without keeping the input; a settings store whose warning says `corrupt_fallback` while no `.bad` file sits beside the path.
- **Regression coverage:** `JsonSettingsStoreTests.AnUnreadableFileKeepsItsTextInABackupBeforeDefaultsOverwriteIt` (regression) seeds a damaged file, flushes, and checks the backup holds the original text; `AReadableFileLeavesNoBackup` pins the other side.

## Disposing the store dropped a save still waiting out its debounce

- **Trigger:** `Replace` or `Save`, then `Dispose` within the 300 ms debounce window (a change made just before shutdown).
- **Observed symptom:** none observed. Found by a code audit and reproduced by a test: the file on disk kept the old value. The audit also said a `Save` after `Dispose` throws `ObjectDisposedException`; it does not, `Timer.Change` on a disposed timer returns false, so the change was dropped without a trace.
- **Cause:** `Dispose` disposed the timers without writing. The disposed debounce timer cancelled the pending write, and nothing recorded that a write was owed.
- **Resolution (2026-10-22):** the store tracks a pending flag, set by `Save` and cleared when `Flush` serializes. `Dispose` waits for a debounce callback already running, then flushes if a save is still owed. A `Save` after `Dispose` flushes at once. Impact before the fix was limited: the settings wrappers call `Flush` themselves at exit, and no code disposed a store directly (audit reading, not re-checked here).
- **Violated invariant:** a change handed to the store reaches the disk, however late in the process it arrives.
- **Recurrence cue:** a `Dispose` that releases a debounce or batching timer without asking whether work is queued behind it.
- **Regression coverage:** `JsonSettingsStoreTests.DisposingTheStoreWritesAPendingDebouncedSave` and `ASaveAfterDisposeIsStillWritten` (regression).

## A follow read that raced the store's own save put older values back

- **Trigger:** an external write to the settings file, then a save by the store itself (a toggle flipped in the UI) landing inside the window between the follow timer's file read and its apply.
- **Observed symptom:** none observed. Found by a code audit of FollowFile. Reasoned, not reproduced: the window is a few milliseconds wide behind a 250 ms settle timer, so the exact interleaving cannot be forced through the store's public surface.
- **Cause:** FollowFile compared the text it read against _lastText before parsing, then applied the parsed value without checking again. A Flush finishing in between wrote newer text and updated _lastText, and the stale read then replaced _current with the older values; the next save would have written them back to disk.
- **Resolution (2026-10-22):** FollowFile records _lastText before it reads the file and, under the same lock that applies the result, drops the read if _lastText has moved. The store's own write notifies the watcher again, so a fresh read follows.
- **Violated invariant:** a read of the file never replaces newer values the store itself wrote.
- **Recurrence cue:** a read-then-apply sequence whose guard runs before a slow step instead of at the moment of the swap.
- **Regression coverage:** none. The two threads cannot be interleaved deterministically without a hook inside the store that exists only for the test; the fix rests on reading. The existing follow tests (AWriteFromOutsideReplacesCurrentAndRaisesChanged, ItsOwnWriteKeepsTheInMemoryInstance) pin that the guard does not block the ordinary paths.