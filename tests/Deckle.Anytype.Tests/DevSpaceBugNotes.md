# Dev space schema bug notes

## Priority read under a key the server no longer serves

- **Trigger:** any digest that shows a priority (`session_start`, `project_overview`, `task_properties`) on a task whose Priorité is set (seen 2026-10-22).
- **Observed symptom:** no priority anywhere. `project_overview` printed no ` · P` on any task of a project although one of them was at priority 5, and the `session_start` header of a task created with priority 0 had no `prio` part. Raw reads of both tasks returned the value under the key `priorite`, in v1 (`properties[].key`) and in v2 (`properties.priorite`).
- **Cause:** `DevSpace.Props.Priorite` held the space's opaque relation id `67c6d714…`, and every reader matches a property by key equality. Writes hid it: a v2 create under the opaque id still stored the priority, and writes under `priorite` land through v1 (option tag key) and v2 (option name), measured on a probe task the same day. Uncertain: whether the server ever served the opaque id on reads, or since when it serves `priorite`; the constant was frozen from an older measurement that this one does not reproduce.
- **Violated invariant:** each key in `DevSpace.Props` is the key the server serves on reads, not merely one it accepts on writes.
- **Recurrence cue:** a property that is set in Anytype but never shows in a digest, while writes to it succeed; a `DevSpace.Props` key that appears in no read of a real object.

Regression: `ReadGesturesTests.TaskPropertiesReadsThePriorityUnderTheKeyTheServerServes`.
