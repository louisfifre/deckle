# Space write lock bug notes

## The write lock polled every I/O failure as if another writer held it

- **Trigger:** a mutating gesture asked for the write scope while opening `write.lock` failed for a reason other than another process holding it, such as a module directory that does not exist.
- **Observed symptom:** the gesture never returned and wrote nothing to the log; the host looked hung. Established by reproduction with a missing directory; which real condition would cause it in the field was not observed.
- **Cause:** `AcquireAsync` caught every `IOException` and treated it as a sharing violation. A missing directory, a path too long or a vanished drive are `IOException`s too, and none of them clears by waiting.
- **Violated invariant:** the lock waits only while another writer holds the file (Win32 sharing or lock violation); any other failure reaches the gesture at once.
- **Recurrence cue:** a retry loop whose `catch` names a broad exception type, with no cap and no check of the error code.

Regression: `SpaceWriteLockTests.AcquireSurfacesAFailureThatIsNotAnotherWriterHoldingTheLock`. The wait for a real holder stays unbounded, as the caller's cancellation token decides how long it is worth waiting.