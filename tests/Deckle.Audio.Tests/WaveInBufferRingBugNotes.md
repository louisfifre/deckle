# waveIn buffer ring bug notes

## Buffers were processed by index instead of in the order the driver filled them

Found by reading the code on 2026-10-21, while looking for why the end of a
dictation was missing. Not reproduced on a microphone, and not seen in a
recording.

- **Trigger.** Two or more buffers done at one wake whose indices straddle the
  wrap of the ring, for example buffer 3 then buffer 0. The post-stop drain is
  the case most exposed: `waveInStop` returns the partial current buffer, which
  can sit at a lower index than the last completed one.
- **Symptom.** Two 50 ms chunks swapped in the PCM and in the frame stream fed to
  the streaming segmenter, at the point of the wrap or at the very end of the
  take. Inferred from the code; no recording was inspected, and how audible a
  50 ms swap is on speech is not established.
- **Cause.** Both the in-loop scan and the drain walked
  `for (i = 0; i < nBuffers; i++)`. The driver returns buffers in the order they
  were queued, and the ring is requeued as buffers are processed, so the
  chronological order is cyclic from the oldest outstanding buffer, not from
  index 0.
- **Violated invariant.** Buffers are consumed in queue order (FIFO), from the
  oldest outstanding one, and each is requeued right after it is taken. A done
  flag is only read for a buffer once every older one has been taken.
- **Recurrence cue.** Any loop over `hdrPtrs` by plain index that appends audio,
  or a requeue that does not follow the order in which buffers were taken.
- **Guarded by.** `WaveInBufferRingTests`, which checks the ordering rule on
  its own, `BuffersAcrossTheWrapAreTakenOldestFirst` for the wrap. Nothing
  exercises `WaveInLoop.Pump` itself: it needs a real device, so a later
  regression that stops using the ring would not be caught.
