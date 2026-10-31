# LoopPulse bug notes

## The loop rollup filled the LogWindow with an empty line a minute

- **Trigger:** any loop that ticks at least once a minute while the LogWindow is open. Seen 2026-10-31 on a run of 0.72.8 where the Anytype health probe (2 ticks a minute, always) and the autocorrect surface changes (up to 127 a minute while typing) kept the rollup firing; the trace of that run held 390 `LoopRollup` rows, `log.jsonl` none.
- **Observed symptom:** the LogWindow showed `loop rollup` once a minute per live loop, with no loop name and no count on the line; the maintainer read it as a loop stuck in a loop and as noise with nothing to act on.
- **Cause (established by reading, and matching the row counts of both files):** `DeckleHotLoopSource.LoopRollup` was declared Informational without the Milestone tag and with the sentence template `"loop rollup"`. `log.jsonl` keeps Warning and above plus milestones, so it left the rollup out as intended, but `LogWindowSink.Wants` admits every operational entry, and the view starts unfiltered. At Informational the only way to hide the rollup in the window was to uncheck the severity that also carries every milestone. The Informational template shape, a sentence alone, is what kept the loop name and the count off the line.
- **Violated invariant:** the rollup is detail (deckle-logging: the record that travels keeps milestones, Warnings and Errors; the rest is detail a person admits or filters). Detail is Verbose and reads as `name={n}` pairs, so the person filters it with the rest of the detail and a line that stays carries its values.
- **Recurrence cue:** an Informational, non-milestone event emitted on a timer or per tick of a hot loop; a `TemplateDoctrine` run that accepts a sentence template on an event whose payload is the whole message.

Regression: `LoopPulseTests.AWindowWithTicksWritesOneRollupAsVerboseDetail`, and `MessageTemplateDoctrineTests.EveryTemplateFollowsTheShapeOfItsLevel` holds the pairs template.
