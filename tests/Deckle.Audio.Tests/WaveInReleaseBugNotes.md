# waveIn release bug notes

## Buffers still queued in the driver were freed at the end of every capture

Seen on 2026-10-30 as a crash of v0.72.5 with exit code `0xC0000374` (heap
corruption, raised in `ntdll`). The Windows event log dates the fault at
18:17:09, about 80 ms after a listening test in Settings > Audio > Recording
released the microphone (18:17:08.930), and before any playback was logged.

- **Trigger.** Any capture that ends while buffers are still queued, which is
  every capture: with 4 buffers of 50 ms, `waveInStop` leaves 3 of them pending
  (`CaptureDrainDetail` reported `pending_buffers=3` on each take).
- **Symptom.** Every take logged `CaptureReleaseDetail` with
  `stage=unprepare_header | mmsys_err=33` (`WAVERR_STILLPLAYING`), 53 times in
  the session that crashed. The crash itself is rare: the corruption is only
  caught when the heap next checks the damaged block. Here that came within
  80 ms of the release, so the playback the user started next did not trigger
  it; which heap call caught it is unknown, as no stack was read.
- **Cause.** The drain in `WaveInLoop.Pump` stopped the device with
  `waveInStop`, which returns only the buffer being filled and leaves the empty
  ones queued. `waveInUnprepareHeader` refused those headers, and
  `MicrophoneCapture` then freed their memory with `FreeHGlobal` while the
  driver still held pointers to it. The refusal codes were ignored until
  2026-10-21, which hid the bug since the capture was written; the failed-start
  path (`ReleaseAfterFailedStart`) already called `waveInReset` first.
- **Violated invariant.** Memory handed to `waveInAddBuffer` is freed only once
  the driver has returned it: `waveInReset` returns every pending buffer, and a
  header is unprepared only after that.
- **Recurrence cue.** A `CaptureReleaseDetail` or `CaptureReleaseFailed` line
  with `stage=unprepare_header` or `stage=close` and `mmsys_err=33`, or any
  release path that frees `hdrPtrs` or `bufPtrs` without a `waveInReset` before
  it.
- **Guarded by.** No automatic test. `Pump` calls winmm directly and needs a
  real device, and the design has no seam in front of it; adding one only for
  this test would be a seam the design does not want. The release incident
  logged on every take is the watch: after the fix, the first take logs
  `CaptureReleaseRecovered` and later takes log no `CaptureReleaseDetail` line.
