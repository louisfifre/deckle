# Hallucination bug notes

## The warm-up's credit line leaked into the first take

Seen on 2026-10-21 on a Release build running whisper.cpp v1.9.1 (bundle
1.9.1, ggml-large-v3, French).

- **Trigger.** The warm-up clip is missing, so the model warms up on silence.
  Whisper decodes that silence as a subtitle credit line such as
  "Sous-titrage Société Radio-Canada". The next take starts while the setting
  that carries context between segments is on.
- **Symptom.** The first utterance of the take delivered a credit line instead
  of, or beside, what was said. The log shows the order of events: the missing
  clip, then a filtered hallucination during the warm-up, then a one-utterance
  streaming take. The text the take delivered is not in the log, because the
  transcription detail was off. It is known from the maintainer's report, not
  from the log.
- **Cause.** whisper.cpp keeps the tokens a call decodes in `prompt_past`, a
  member of the state that `whisper_full` reuses. The next call starts from
  them unless `no_context` is set, and Deckle cleared `no_context` on every
  call. So the warm-up's credit line primed the take's decoder. Setting
  `no_context` on the warm-up alone would not help: whisper.cpp clears the
  context at the start of the call, then writes the call's own output back
  into it. Read in the v1.9.1 source, not reproduced at run time.
  Separately, the filter compared the whole utterance with exact strings, so
  a credit line with other punctuation, case or spacing, or one segment among
  real speech, went through.
- **Violated invariant.** A call continues the decoder context only when the
  pipeline carries the previous utterance's text into it. The warm-up, the
  first utterance of a take, a call after a failure, a rejection or an empty
  utterance, and a call after a filtered hallucination all start clean. And a
  segment made only of a catalogue line is never delivered, whatever its
  punctuation, case or spacing.
- **Recurrence cue.** A new caller of `IAsrBackend.TranscribeAsync` that sets
  `ContinuesPreviousCall` without carrying the previous text, a mapper change
  that derives `no_context` from the setting alone, a native upgrade that
  changes how `prompt_past` survives a call, or a credit line in a take whose
  log shows `TranscribeHallucinationFiltered` in the call just before it.
- **Guarded by.** `WhisperParamsMapperTests.OnlyACallThatContinuesThePreviousOneKeepsItsDecodedContext`,
  `KnownHallucinationsVariantTests` in this folder, and
  `LiveTranscriptionPipelineTests.TheFirstUtteranceAfterTheWarmUpDoesNotContinueTheWarmUpCall`
  in `Deckle.Transcription.Tests`. The backend's own guard after a filtered
  call has no test, because it sits behind the native call. The filter
  reaches every pipeline because they all deliver `TranscriptionResult.FullText`,
  which the backend builds once. That holds by construction and has no test
  per pipeline. A credit line glued to real speech inside one segment is still
  delivered, and no test claims otherwise.
