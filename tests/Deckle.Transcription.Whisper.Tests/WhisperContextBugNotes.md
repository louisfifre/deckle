# Whisper context bug notes

## The GPU switch never reached whisper

- **Trigger:** Turning off "GPU acceleration" on the Dictation settings page, then dictating, even after a restart.
- **Observed symptom:** Transcription kept running on Vulkan. On another machine where Deckle worked badly there was no way to try the CPU, and the log showed no sign of which device ran. Whether the GPU caused that machine's trouble is not established.
- **Cause:** `EngineSettings.UseGpu` was bound by the page and the view model and persisted, but no runtime code read it: `WhisperBackend.LoadModelSync` hard-coded `ctxParams.use_gpu = 1`, and the load-start detail hard-coded `use_gpu=1` in its message. Established by reading the code; the other machine's trouble was reported, not reproduced.
- **Violated invariant:** A setting the page exposes governs the behavior it names. A load-time setting is read from the live settings at each model load.
- **Recurrence cue:** A native init parameter assigned a literal in `WhisperBackend.Model.cs` instead of going through `WhisperContextParamsMapper`, or a new `EngineSettings` property with no reader outside the view model, the page and settings search.
- **Regression coverage:** `WhisperContextParamsMapperTests.DisabledGpuSettingTurnsOffUseGpu` and `EnabledGpuSettingTurnsOnUseGpu` pin the mapping. Nothing covers the call site itself: it needs the native library.

## The detected backend keyed on a once-per-process table

- **Trigger:** A model reload in the same process, after the five-minute idle unload, or a load with the GPU switch off.
- **Observed symptom:** None observed yet. The retained log on the development machine holds a single load, so neither case has been seen in a real log.
- **Cause:** `HandleWhisperLog` set the backend to Vulkan when a line started with `ggml_vulkan:`. ggml prints that device table when its process-global registry first initializes, not at each context creation. Inferred consequences: a reload would report CPU while Vulkan ran, and a CPU-only load could report Vulkan, since the registry enumerates its devices whatever `use_gpu` says. Both rest on ggml's behavior as read from its source, not on a run.
- **Violated invariant:** The reported device is the one the current context runs on, read from what whisper says at that context's creation.
- **Recurrence cue:** Backend detection keyed on a ggml line instead of `whisper_backend_init_gpu`'s `using <device> backend` and `failed to initialize` lines, or the Vulkan device names kept per load instead of per process.
- **Regression coverage:** `WhisperComputeDeviceObserverTests.ReloadStillNamesTheGpuWithoutTheVulkanTable` and `GpuOffReportsTheCpuAsAskedDespiteTheVulkanTable`.

## A GPU init failure stayed below the visible log

- **Trigger:** The Vulkan device is found but its backend fails to initialize.
- **Observed symptom:** None observed. whisper.cpp then builds the context on the CPU and the load succeeds.
- **Cause:** `WhisperNativeLogCompactor` consumes every `whisper_backend_init_gpu:` line into one Verbose summary, so the native error line never reached the Warning/Error path. Established by reading the code; the failure line's wording comes from whisper.cpp's source.
- **Violated invariant:** A degradation a person must act on, here a GPU requested and not used, reaches the log without the Transcription detail switched on.
- **Recurrence cue:** A fallback decided by whisper.cpp that only shows in a compacted Verbose line.
- **Regression coverage:** `WhisperComputeDeviceObserverTests.GpuInitFailureWarnsOncePerIncidentUntilTheGpuAnswers` and `DeviceMilestoneReachesTheLogWhenTranscriptionDetailIsOff`.
