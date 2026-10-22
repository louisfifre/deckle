# Phantom utterance bug notes

## The last words of an utterance were dropped (ending noise pad)

Seen on 2026-10-21 in replays of one captured utterance, Whisper large-v3,
French, VAD trim on (30 ms speech pad).

- **Trigger.** The backend receives audio that ends 30 ms after the last
  speech, i.e. near-digital silence right behind the final word.
- **Symptom.** The text stops mid-sentence ("... me dire ce que" instead of
  "... me dire ce que tu en penses."). Not deterministic: the same audio
  replayed 8 times gave the full ending 3 times out of 8.
- **Cause (inferred, not proven inside Whisper).** The near-digital-silence
  tail lets the decoder emit end-of-text early. Replay evidence: appending
  500 ms of exact digital silence fixed 0/8; appending 500 ms of noise at
  about -60 dBFS RMS fixed 8/8.
- **False lead.** The single 30.0 s segment seen in the logs for these
  utterances is a cosmetic timestamp artefact, not the cause of the
  truncation.
- **Violated invariant.** Every utterance reaches the backend followed by a
  low-level noise tail, never by digital silence or by a bare trim edge.
- **Recurrence cue.** A delivered take ending mid-sentence while the log shows
  `UtteranceSentToBackend` with `pad_ms=0` or no `pad_ms`, or with the pad
  present and the text still cut (then the level or the length of
  `TrailingNoisePad` needs re-tuning against a replay).
- **Guarded by.** `TrailingNoisePadTests` (length, level, determinism, input
  untouched) and
  `LiveTranscriptionPipelineTests.EveryUtteranceReachesTheBackendWithANoiseTailAfterItsSpeech`.
  Not guarded: the end-of-text behaviour itself needs the real model, so no
  automated test reproduces the truncation; the fix rests on the 8-run
  replay. The pad is not applied to `BackendAudio` (corpus record) and the
  phantom filter judges the unpadded duration.

## A noise blip before Stop was pasted as words

Seen on 2026-10-21 in a streaming take (session of 13:08:50 in the
diagnostics log), Whisper large-v3, French, preprocessing on.

- **Trigger.** A short non-speech sound about 1.5 s before the Stop press,
  loud enough for the energy segmenter. At Stop the segmenter flushed it as a
  pending utterance of about 0.55 s (8 voiced frames, 11 kept frames).
- **Symptom.** A line of words nobody said, delivered after the real
  sentence. The log shows one extra utterance: preprocessing lifted it by
  +16 dB (input -35.6 dBFS), Whisper decoded one 0.6 s segment of 12 text
  tokens with avg_p 0.601, min_p 0.079 and no_speech 0.000. The real
  utterance before it had avg_p 0.85 to 0.94. The delivered text is not in the
  log; it is known from the maintainer's report.
- **Cause.** Nothing judged the decode against the audio it came from. The
  only gates were `MinUtteranceMs` (250 ms, passed) and the fixed
  `KnownHallucinations` list (a phantom is not on it). The preprocessor then
  made a quiet blip look like speech to the model. Why Whisper reports
  no_speech 0.000 on such a blip is not established.
- **Violated invariant.** A short utterance whose decode no speaker could have
  produced from that audio (too many tokens for its duration, or an average
  and a minimum token probability both collapsed) is not delivered.
- **Recurrence cue.** A `UtterancePhantomRejected` line in the log is the fix
  working. The bug is back when a delivered take carries a short extra
  utterance with 12 or more tokens per second of audio or avg_p under 0.7, or
  when a clear one-word answer disappears and the log shows that event for it
  (thresholds too tight; they are constants in `PhantomUtteranceFilter`).
- **Guarded by.** `PhantomUtteranceFilterTests` (the observed numbers are
  rejected, a clear short answer and a long doubtful utterance are kept) and
  `LiveTranscriptionPipelineTests.APhantomUtteranceIsLeftOutOfTheDeliveredText`.
  Not guarded: the segment sink has already streamed the phantom segment to
  `NewSegment` listeners before the verdict, so a live display may show it
  briefly. The thresholds come from one observed case and the known speech
  rates, not from a corpus.
