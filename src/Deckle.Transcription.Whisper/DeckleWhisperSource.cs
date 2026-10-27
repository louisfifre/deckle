using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription.Whisper;

// Whisper backend provider: the whisper.cpp native runtime (identity, install,
// log hook), the model context it builds (load, compute device) and the
// native decode call. The transcription engine above it logs through
// Deckle-Whisp; the two meet on the model load, where Deckle-Whisp tells the
// engine's story ("Loading model", "Model loaded") and this provider the
// native facts behind it (which runtime, which file, which device, why not).
//
// Transcript text never enters an event here: native log lines are emitted
// with every print channel of whisper_full closed, and segment events carry
// counts and timings only.
[EventSource(Name = "Deckle-Whisper")]
public sealed class DeckleWhisperSource : DeckleEventSource
{
    public static readonly DeckleWhisperSource Log = new();

    private DeckleWhisperSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtNativeRuntimeLoaded                 = 1;
    public const int EvtNativeRuntimeLoadedDetail           = 2;
    public const int EvtNativeRuntimeSystemInfo             = 3;
    public const int EvtLogHookUnavailable                  = 4;
    public const int EvtLogHookLineFailed                   = 5;
    public const int EvtLogHookLineFailedDetail             = 6;
    public const int EvtNativeLogLine                       = 7;
    public const int EvtNativeLogWarning                    = 8;
    public const int EvtNativeLogError                      = 9;
    public const int EvtModelLoadStarting                   = 10;
    public const int EvtModelLoadStartingDetail             = 11;
    public const int EvtModelLoadFailed                     = 12;
    public const int EvtModelLoadFailedDetail               = 13;
    public const int EvtModelOnGpu                          = 14;
    public const int EvtModelOnCpu                          = 15;
    public const int EvtModelGpuUnavailable                 = 16;
    public const int EvtModelDeviceUnknown                  = 17;
    public const int EvtModelLoadComplete                   = 18;
    public const int EvtTranscribeFailed                    = 19;
    public const int EvtSegmentCallbackFailed               = 20;
    public const int EvtSegmentCallbackFailedDetail         = 21;
    public const int EvtSegmentCallbackRecovered            = 22;
    public const int EvtModelFileProbeFailed                = 23;
    public const int EvtNativeRuntimeProvisionStarted       = 24;
    public const int EvtNativeRuntimeProvisionEnded         = 25;
    public const int EvtNativeRuntimeProvisionFailed        = 26;
    public const int EvtNativeRuntimeProvisionFailedDetail  = 27;
    public const int EvtNativeRuntimeCopied                 = 28;
    public const int EvtNativeRuntimeCopiedDetail           = 29;
    public const int EvtNativeRuntimeCleanupFailed          = 30;
    public const int EvtNativeRuntimeProbeFailed            = 31;
    public const int EvtNativeRuntimeLoadFailed             = 32;
    public const int EvtNativeRuntimeLoadCheckPassed        = 33;
    public const int EvtNativeRuntimeLoadCheckFailed        = 34;
    public const int EvtNativeBackendsLoaded                = 35;
    public const int EvtNativeBackendVulkanUnavailable      = 36;
    public const int EvtNativeBackendsNone                  = 37;
    public const int EvtNativeRuntimeStaleRemoved           = 38;
    public const int EvtNativeRuntimeStaleRemoveFailed      = 39;

    // ── Native runtime identity ─────────────────────────────────────────

    // Once per process. version is what libwhisper.dll itself reports
    // ("unknown" for a build without whisper_version), bundle the release
    // this app expects: a hand-imported runtime can differ. The whole identity
    // travels in the payload, since a Verbose mirror would stay out of the
    // log a reader on another machine gets.
    [Event(EvtNativeRuntimeLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Loaded the whisper.cpp runtime")]
    public void NativeRuntimeLoaded(string version, string bundle)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeLoaded, version, bundle);
    }

    [Event(EvtNativeRuntimeLoadedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | size_bytes={1} | file_version={2} | modified={3}")]
    public void NativeRuntimeLoadedDetail(string path, long size_bytes, string file_version, string modified)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeLoadedDetail, PathRedaction.Redact(path), size_bytes, file_version, modified);
    }

    // Read after the first model init, when ggml has already registered its
    // backends: asking earlier would start the Vulkan device enumeration at
    // app startup.
    [Event(EvtNativeRuntimeSystemInfo,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "features={0}")]
    public void NativeRuntimeSystemInfo(string features)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeSystemInfo, features);
    }

    // An identity read that failed: the runtime still works, only one field of
    // NativeRuntimeLoaded or its detail is missing. probe (closed vocabulary):
    // version | file_details | system_info.
    [Event(EvtNativeRuntimeProbeFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "probe={0} | ex_type={1} | ex_message={2}")]
    public void NativeRuntimeProbeFailed(string probe, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeProbeFailed, probe, ex_type, PathRedaction.Redact(ex_message));
    }

    // ── Native runtime loading ──────────────────────────────────────────

    // The first catalog DLL that did not load, with the import it misses
    // when the loader could tell. Transcription stops here: the P/Invoke
    // resolver throws right after. win32_error: 126 an import is missing,
    // 193 not a 64-bit DLL, 1114 its initialization failed.
    [Event(EvtNativeRuntimeLoadFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The whisper.cpp runtime could not be loaded, so transcription cannot run\ndll={0} | missing_import={1} | win32_error={2} | summary={3}")]
    public void NativeRuntimeLoadFailed(string dll, string missing_import, int win32_error, string summary)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeLoadFailed, dll, missing_import, win32_error, PathRedaction.Redact(summary));
    }

    // The setup wizard's question, "does the installed runtime load here?",
    // answered. vulkan is false on a machine without the Vulkan loader: the
    // runtime still loads and transcribes on the CPU.
    [Event(EvtNativeRuntimeLoadCheckPassed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Checked that the whisper.cpp runtime loads on this machine")]
    public void NativeRuntimeLoadCheckPassed(bool vulkan)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeLoadCheckPassed, vulkan);
    }

    [Event(EvtNativeRuntimeLoadCheckFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The installed whisper.cpp runtime does not load on this machine\ndll={0} | missing_import={1} | win32_error={2} | summary={3}")]
    public void NativeRuntimeLoadCheckFailed(string dll, string missing_import, int win32_error, string summary)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeLoadCheckFailed, dll, missing_import, win32_error, PathRedaction.Redact(summary));
    }

    // ── ggml backends ───────────────────────────────────────────────────

    // Once per process, before the first model load. backends is ggml's own
    // names, comma-separated ("CPU,Vulkan"); devices counts what they expose.
    [Event(EvtNativeBackendsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Loaded the ggml backends")]
    public void NativeBackendsLoaded(string backends, long devices)
    {
        if (IsEnabled()) WriteEvent(EvtNativeBackendsLoaded, backends, devices);
    }

    // The fallback the bundle is built for: no Vulkan loader on this machine
    // (or the backend refused the device), so whisper.cpp runs on the CPU.
    [Event(EvtNativeBackendVulkanUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Vulkan backend is not available, so transcription runs on the CPU\nbackends={0}")]
    public void NativeBackendVulkanUnavailable(string backends)
    {
        if (IsEnabled()) WriteEvent(EvtNativeBackendVulkanUnavailable, backends);
    }

    // Not even the CPU backend answered: the folder holds no loadable
    // ggml-cpu-*.dll, and the model load that follows cannot succeed.
    [Event(EvtNativeBackendsNone,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "No ggml backend could be loaded, so transcription cannot run\nnative_dir={0}")]
    public void NativeBackendsNone(string native_dir)
    {
        if (IsEnabled()) WriteEvent(EvtNativeBackendsNone, PathRedaction.Redact(native_dir));
    }

    // ── Native log hook ─────────────────────────────────────────────────

    // Without the hook, whisper's lines are lost and the compute device can no
    // longer be read from them: the device then reports as unknown.
    [Event(EvtLogHookUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The whisper.cpp log hook could not be installed, so its messages and the compute device are unknown\nex_type={0} | ex_message={1}")]
    public void LogHookUnavailable(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLogHookUnavailable, ex_type, PathRedaction.Redact(ex_message));
    }

    // Once per process: a handler that throws on one line usually throws on
    // the next. The raw failures follow as the gated detail below.
    [Event(EvtLogHookLineFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A whisper.cpp log line could not be handled\nex_type={0} | ex_message={1}")]
    public void LogHookLineFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLogHookLineFailed, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtLogHookLineFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "failures={0} | ex_type={1} | ex_message={2}")]
    public void LogHookLineFailedDetail(int failures, string ex_type, string ex_message)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtLogHookLineFailedDetail, failures, ex_type, PathRedaction.Redact(ex_message));
    }

    // One per native line outside the init phases the compactor folds: the
    // per-call lines of whisper_full among them, hence the Transcription gate.
    // level is ggml's: 1=Debug, 2=Info, 5=Cont.
    [Event(EvtNativeLogLine,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "level={0} | line={1}")]
    public void NativeLogLine(int level, string line)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtNativeLogLine, level, PathRedaction.Redact(line));
    }

    // The native line is the cause, so it travels in the payload. whisper.cpp
    // prints no transcript at these levels with the print channels closed.
    [Event(EvtNativeLogWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper.cpp reported a warning\nmessage={0}")]
    public void NativeLogWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeLogWarning, PathRedaction.Redact(message));
    }

    [Event(EvtNativeLogError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper.cpp reported an error\nmessage={0}")]
    public void NativeLogError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeLogError, PathRedaction.Redact(message));
    }

    // ── Model load ──────────────────────────────────────────────────────

    // Every load, ungated: which file, whether the settings asked for the GPU
    // and which runtime builds the context are what a log from another machine
    // must answer first. runtime is the version libwhisper.dll reports.
    [Event(EvtModelLoadStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Loading the Whisper model")]
    public void ModelLoadStarting(string model, bool use_gpu, string runtime)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadStarting, model, use_gpu, runtime);
    }

    [Event(EvtModelLoadStartingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | file_mb={1:F1} | gpu_device={2} | flash_attn={3} | log_hook={4}")]
    public void ModelLoadStartingDetail(string path, double file_mb, int gpu_device, bool flash_attn, bool log_hook)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadStartingDetail, PathRedaction.Redact(path), file_mb, gpu_device, flash_attn, log_hook);
    }

    // Once per incident: the engine retries the load at every take, and its
    // own ModelUnavailable/ModelRecovered pair is the visible incident, with
    // the same reason through ModelLoadResult.ErrorReason. This mirror keeps
    // the native facts behind it. reason (closed vocabulary): file_not_found |
    // init_failed. device is the one the native init was building on,
    // "unknown" when no log line could say.
    [Event(EvtModelLoadFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0} | model={1} | device={2}")]
    public void ModelLoadFailed(string reason, string model, string device)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailed, reason, model, device);
    }

    // Every failed attempt. failed_device is the GPU whisper gave up on during
    // that init, empty when none.
    [Event(EvtModelLoadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0} | path={1} | elapsed_ms={2} | use_gpu={3} | failed_device={4}")]
    public void ModelLoadFailedDetail(string reason, string path, long elapsed_ms, bool use_gpu, string failed_device)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailedDetail, reason, PathRedaction.Redact(path), elapsed_ms, use_gpu, failed_device);
    }

    // The compute device of a freshly loaded context. The device name tells a
    // discrete GPU from an integrated one.
    [Event(EvtModelOnGpu,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Whisper model runs on the GPU")]
    public void ModelOnGpu(string device_name, string device)
    {
        if (IsEnabled()) WriteEvent(EvtModelOnGpu, device_name, device);
    }

    [Event(EvtModelOnCpu,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Whisper model runs on the CPU, as the settings ask")]
    public void ModelOnCpu()
    {
        if (IsEnabled()) WriteEvent(EvtModelOnCpu);
    }

    // whisper.cpp falls back to the CPU on its own when no GPU device answers
    // or the GPU backend fails to initialize; the load still succeeds. Once
    // per incident, closed by the next ModelOnGpu or ModelOnCpu. reason
    // (closed vocabulary): no_gpu_found | gpu_init_failed. device is the one
    // whisper gave up on, empty for no_gpu_found.
    [Event(EvtModelGpuUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The GPU could not be used, the Whisper model runs on the CPU\nreason={0} | device={1}")]
    public void ModelGpuUnavailable(string reason, string device)
    {
        if (IsEnabled()) WriteEvent(EvtModelGpuUnavailable, reason, device);
    }

    // The GPU was asked for and no native line said what whisper did with it,
    // so neither GPU nor CPU can be claimed. Once per incident. reason (closed
    // vocabulary): log_hook_unavailable | device_line_not_recognized, the
    // second meaning whisper.cpp changed the wording the observer reads.
    [Event(EvtModelDeviceUnknown,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Whisper compute device is unknown\nreason={0}")]
    public void ModelDeviceUnknown(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtModelDeviceUnknown, reason);
    }

    // Every successful load, including those inside an open incident:
    // fallback_reason repeats the incident's cause, empty when none.
    [Event(EvtModelLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load_ms={0} | backend={1} | device={2} | device_name={3} | use_gpu={4} | log_hook={5} | fallback_reason={6}")]
    public void ModelLoadComplete(
        long load_ms,
        string backend,
        string device,
        string device_name,
        bool use_gpu,
        bool log_hook,
        string fallback_reason)
    {
        if (IsEnabled())
            WriteEvent(EvtModelLoadComplete, load_ms, backend, device, device_name, use_gpu, log_hook, fallback_reason);
    }

    // ── Decode ──────────────────────────────────────────────────────────

    // reason (closed vocabulary): no_context (no model loaded when the call
    // came, status -1) | native_error (whisper_full returned status). The
    // same reason travels up in TranscriptionResult.ErrorReason, and the
    // engine's failure event is the visible incident.
    [Event(EvtTranscribeFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "reason={0} | status={1}")]
    public void TranscribeFailed(string reason, int status)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailed, reason, status);
    }

    // The callback stops at the throwing segment, so the rest of that batch is
    // missing from the result. Once per incident, closed by the first call
    // whose segments all land.
    [Event(EvtSegmentCallbackFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The segment callback failed, segments may be missing\nex_type={0} | ex_message={1}")]
    public void SegmentCallbackFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackFailed, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtSegmentCallbackFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "failures={0} | ex_type={1} | ex_message={2}")]
    public void SegmentCallbackFailedDetail(int failures, string ex_type, string ex_message)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtSegmentCallbackFailedDetail, failures, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtSegmentCallbackRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The segment callback works again")]
    public void SegmentCallbackRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackRecovered);
    }

    // ── Model files ─────────────────────────────────────────────────────

    // A probe that throws reads as "no usable model here"; the fallback or
    // load-failure Warning that follows carries what the user gets.
    [Event(EvtModelFileProbeFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | ex_type={1} | ex_message={2}")]
    public void ModelFileProbeFailed(string path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelFileProbeFailed, PathRedaction.Redact(path), ex_type, PathRedaction.Redact(ex_message));
    }

    // ── Native runtime install ──────────────────────────────────────────

    [Event(EvtNativeRuntimeProvisionStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Installing the whisper.cpp runtime")]
    public void NativeRuntimeProvisionStarted(string version)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeProvisionStarted, version);
    }

    // outcome (closed vocabulary): completed | cancelled. A failure ends on
    // NativeRuntimeProvisionFailed instead.
    [Event(EvtNativeRuntimeProvisionEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The whisper.cpp runtime install ended")]
    public void NativeRuntimeProvisionEnded(string version, string outcome, int extracted, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeProvisionEnded, version, outcome, extracted, elapsed_ms);
    }

    // reason (closed vocabulary): placeholder_url | download_failed |
    // bundle_incomplete | load_check_failed | exception. For download_failed,
    // ex_message is the downloader's own error text; for load_check_failed,
    // the sentence naming the DLL that does not load (the check's own
    // Warning carries the fields).
    [Event(EvtNativeRuntimeProvisionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The whisper.cpp runtime could not be installed, so transcription cannot run\nreason={0} | version={1} | ex_type={2} | ex_message={3}")]
    public void NativeRuntimeProvisionFailed(string reason, string version, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeProvisionFailed, reason, version, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtNativeRuntimeProvisionFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "url={0} | extracted={1} | missing={2} | elapsed_ms={3}")]
    public void NativeRuntimeProvisionFailedDetail(string url, int extracted, string missing, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeProvisionFailedDetail, url, extracted, missing, elapsed_ms);
    }

    // A runtime picked by hand: missing counts the catalog DLLs still absent
    // after the copy, so a partial import shows here, before any model load.
    [Event(EvtNativeRuntimeCopied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Copied the whisper.cpp runtime from a folder")]
    public void NativeRuntimeCopied(int copied, int missing)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeCopied, copied, missing);
    }

    [Event(EvtNativeRuntimeCopiedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "source={0} | missing={1}")]
    public void NativeRuntimeCopiedDetail(string source, string missing)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeCopiedDetail, source, missing);
    }

    // A best-effort delete that failed: the leftover file is overwritten by
    // the next attempt, so nothing the user gets changes.
    [Event(EvtNativeRuntimeCleanupFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | ex_type={1} | ex_message={2}")]
    public void NativeRuntimeCleanupFailed(string path, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeCleanupFailed, PathRedaction.Redact(path), ex_type, PathRedaction.Redact(ex_message));
    }

    // Every provisioning leaves the native folder holding the catalog alone.
    // names lists the DLLs removed (file names, no path), comma-separated:
    // an older bundle's files (1.9.1 shipped libwhisper.dll and the MinGW
    // runtime) and any stray DLL. Written only when something was removed.
    [Event(EvtNativeRuntimeStaleRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Removed DLLs the speech engine catalog no longer names from the native folder")]
    public void NativeRuntimeStaleRemoved(int removed, string names)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeStaleRemoved, removed, names);
    }

    // Degraded, not stopped: the catalog is in place, but ggml enumerates the
    // ggml-*.dll of this folder as backends, so a stale one it cannot delete
    // may still be loaded beside the current ones.
    [Event(EvtNativeRuntimeStaleRemoveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A DLL the catalog no longer names could not be removed from the native folder, so it stays beside the current runtime\nname={0} | ex_type={1} | ex_message={2}")]
    public void NativeRuntimeStaleRemoveFailed(string name, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNativeRuntimeStaleRemoveFailed, name, ex_type, PathRedaction.Redact(ex_message));
    }
}
