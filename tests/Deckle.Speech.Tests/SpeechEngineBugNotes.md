# Speech engine bug notes

## A Speak racing Dispose could start a request on a released backend

- **Trigger:** `Speak` on one thread reading the disposed flag as false, then `Dispose` running in full on another thread before `Speak` takes the lock.
- **Observed symptom:** none observed. Found on 2026-10-22 by a code audit of `SpeechEngine`. Reasoned, not reproduced. The consequence is also unproven: a released backend most likely throws, which the engine logs as `synthesis_failed`; whether a backend can touch released native state was not checked.
- **Cause:** `Speak` read `_disposed` before the lock, and `Dispose` set it outside the lock. The window between the check and the lock let `Dispose` cancel and release first; `Speak` then created a fresh, never-cancelled token source and ran synthesis against the released backend.
- **Resolution (2026-10-22):** `Speak` checks the flag under the lock, and `Dispose` sets it under the same lock, which also makes a second `Dispose` a no-op without a race. A `Speak` that wins the lock first gets a token source `Dispose` then cancels.
- **Violated invariant:** after `Dispose` returns, no new request starts, and a request that started before it is cancelled.
- **Recurrence cue:** a disposed flag read outside the lock that guards the state `Dispose` tears down.
- **Regression coverage:** none. The two threads cannot be interleaved deterministically without a hook inside the engine that exists only for the test; the fix rests on reading. The existing `SpeechEngineTests` pin that the ordinary Speak and Dispose paths are unchanged.
