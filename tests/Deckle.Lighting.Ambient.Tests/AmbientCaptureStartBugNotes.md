# Ambient capture start bug notes

## The capture's first frame was dropped before the sampler existed

- **Trigger:** Ambient lighting started on a still screen (nothing moving, the pointer at rest). The engine called `ScreenCaptureService.Start`, which starts the capture loop on the thread pool, and only then built its `FrameSampler` and attached `OnFrameArrived`.
- **Observed symptom:** After Ambient Light was turned on, the first preview frame took a long time to arrive (Louis, 2026-11-07). The link between that symptom and this cause is inferred from the code, not measured: no log of a slow start was read. `PipelineStarted` now carries `startup_ms` and `first_frame_ms` so the next slow start says which part was slow.
- **Cause:** Desktop Duplication returns the whole desktop on the first acquire after the duplication is created, then only what changes. When the loop's first acquire returned before the handler was attached, `FrameArrived` had no subscriber and the frame was released unread. Established by reading `ScreenCaptureService.Lifecycle.cs` and `AmbientEngine.Lifecycle.cs`. A second factor, also from the code: the Settings preview drew nothing until the engine reached Running, after the whole bridge handshake.
- **Violated invariant:** A capture's consumer is in place before the capture can deliver its first frame. The trap lives as a comment on `AmbientEngine.EnsureSampler` (`AmbientEngine.FirstFrame.cs`) and on the handler attachment in `StartAsync`.
- **Recurrence cue:** A `ScreenCaptureService` started before its `FrameArrived` handler is attached, or a consumer built after `Start` returns, whether in the engine or the preview scan.
- **Regression coverage:** none yet. The race needs a live Desktop Duplication session and the suite cannot start one. The engine has no seam that hands it a capture double.
