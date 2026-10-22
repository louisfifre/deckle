using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// Vision module provider: screen capture (DXGI Output Duplication) and frame
// sampling (FrameSampler).
[EventSource(Name = "Deckle-Vision")]
public sealed class DeckleVisionSource : DeckleEventSource
{
    public static readonly DeckleVisionSource Log = new();

    private DeckleVisionSource() { }

    public const int EvtScreenCaptureStarting           = 1;
    public const int EvtCaptureSessionConfigured        = 2;
    // 3 retired.
    // 4 retired.
    // 5 retired.
    public const int EvtTargetMonitorResolved           = 6;
    // 7 retired.
    // 8 retired.
    // 9 retired.
    public const int EvtScreenCaptureStoppedDetail      = 10;
    public const int EvtAccessLostRecovering            = 11;
    // 12 retired.
    public const int EvtAcquireFrameFailed              = 13;
    // 14 retired.
    // 15 retired.
    public const int EvtReleaseFrameNonZero             = 16;
    // 17 retired.
    public const int EvtDuplicationResizeDetected       = 18;
    public const int EvtDuplicationRecreated            = 19;
    // 20 retired.
    public const int EvtSamplerInitialized              = 21;
    // 22 retired.
    // 23 retired.
    public const int EvtSecureDesktopRecovering         = 24;
    public const int EvtHeartbeat                       = 25;
    public const int EvtCaptureFormatRenegotiated       = 26;
    public const int EvtCaptureFormatRenegotiatedDetail = 27;

    // Event ids are public in the ETW manifest: append new ones, never reuse
    // an id after deleting an event.
    // 28 retired.
    // 29 retired.
    // 30 retired.
    // 31 retired.
    // 32 retired.
    public const int EvtSamplerMapFailedDetail          = 33;
    // 34 retired.
    // 35 retired.
    // 36 retired.
    public const int EvtDuplicationRecreateRecovered     = 37;
    public const int EvtDuplicationRecreateEpisodeDetail = 38;
    // 39 retired.
    public const int EvtScreenCaptureStarted            = 40;
    public const int EvtCaptureStartFailed              = 41;
    public const int EvtCaptureLoopWaitFailed           = 42;
    public const int EvtScreenCaptureStopped            = 43;
    public const int EvtDeviceLost                      = 44;
    public const int EvtDuplicationRecreateAttemptFailed = 45;
    public const int EvtTextureQueryFailedDetail        = 46;
    public const int EvtFrameConsumerThrewDetail        = 47;
    public const int EvtDuplicationRecreateAttemptFailedDetail = 48;
    public const int EvtSamplerProcessFailedDetail      = 49;
    public const int EvtFrameOwnershipRecoveryAbandoned = 50;
    public const int EvtDuplicationRecreateAbandoned    = 51;
    public const int EvtCaptureLoopCrashed              = 52;
    public const int EvtCaptureLoopEnded                = 53;
    public const int EvtAcquireFrameIncident            = 54;
    public const int EvtAcquireFrameRecovered           = 55;
    public const int EvtCaptureLoopStopTimedOut         = 56;
    public const int EvtSubscriberThrew                 = 57;
    public const int EvtCaptureCleanupFailed            = 58;
    public const int EvtHdrDetectionFailed              = 59;
    public const int EvtMonitorNotFound                 = 60;

    // Capture-loop detail belongs to the Ambient workflow while it runs; the
    // scoped form leaves it ungoverned outside that workflow.
    [NonEvent]
    private bool IsAmbientDetailEnabled(Keywords keywords)
        => OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Ambient, this,
            EventLevel.Verbose, (EventKeywords)keywords);

    // ── Capture session lifecycle ───────────────────────────────────────

    [Event(EvtScreenCaptureStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture starting")]
    public void ScreenCaptureStarting()
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStarting);
    }

    // The one line that names the GPU, the monitor and the negotiated
    // surface: a log read on another machine has nothing else to go on when
    // the lamps stay dark.
    [Event(EvtScreenCaptureStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture started | adapter={0} | feature_level={1} | monitor={2} | size={3}x{4} | format={5} | hdr={6} | rate_hz={7}")]
    public void ScreenCaptureStarted(string adapter, string feature_level, string monitor,
        int width, int height, string format, string hdr_state, int rate_hz)
    {
        if (IsEnabled())
            WriteEvent(EvtScreenCaptureStarted, adapter, feature_level, monitor,
                width, height, format, hdr_state, rate_hz);
    }

    [Event(EvtCaptureSessionConfigured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "start | hmon=0x{0:X} | size={1}x{2} | format={3} | hdr={4} | peak_lum={5:F0} | timeout_ms={6} | throttle_ms={7}")]
    public void CaptureSessionConfigured(long hmon, int width, int height, string format, string hdr_state, double peak_lum, int timeout_ms, int throttle_ms)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose,
                (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
            WriteEvent(EvtCaptureSessionConfigured, hmon, width, height, format, hdr_state, peak_lum, timeout_ms, throttle_ms);
    }

    // stage names the step that threw (resolve_monitor, find_output,
    // create_device, duplicate_output, start_loop).
    [Event(EvtCaptureStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture failed to start | stage={0} | hr=0x{1:X8} | ex_type={2} | ex_message={3}")]
    public void CaptureStartFailed(string stage, int hr, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureStartFailed, stage, hr, ex_type, ex_message);
    }

    [Event(EvtTargetMonitorResolved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "target monitor resolved | device_name={0} | hmon=0x{1:X}")]
    public void TargetMonitorResolved(string device_name, long hmon)
    {
        if (IsEnabled()) WriteEvent(EvtTargetMonitorResolved, device_name, hmon);
    }

    [Event(EvtMonitorNotFound,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Selected monitor not found, capturing the primary monitor instead | requested={0}")]
    public void MonitorNotFound(string requested)
    {
        if (IsEnabled()) WriteEvent(EvtMonitorNotFound, requested);
    }

    [Event(EvtCaptureLoopWaitFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture loop wait threw, continuing shutdown | ex_type={0} | ex_message={1}")]
    public void CaptureLoopWaitFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLoopWaitFailed, ex_type, ex_message);
    }

    // The device is released right after this line, possibly under a loop
    // still inside a DXGI call: the next crash or hang reads from here.
    [Event(EvtCaptureLoopStopTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Capture loop did not stop in time, releasing the device anyway | elapsed_ms={0}")]
    public void CaptureLoopStopTimedOut(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLoopStopTimedOut, elapsed_ms);
    }

    // reason: a CaptureStopReasons value (requested, device_lost,
    // frame_ownership_abandoned, recreate_abandoned or loop_crashed).
    [Event(EvtScreenCaptureStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture stopped | reason={0} | duration_ms={1}")]
    public void ScreenCaptureStopped(string reason, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStopped, reason, duration_ms);
    }

    [Event(EvtScreenCaptureStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "stop | frames={0} | duration_ms={1} | fps_avg={2:F1}")]
    public void ScreenCaptureStoppedDetail(long frames, long duration_ms, double fps_avg)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStoppedDetail, frames, duration_ms, fps_avg);
    }

    [Event(EvtCaptureCleanupFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "cleanup step failed | step={0} | ex_type={1} | ex_message={2}")]
    public void CaptureCleanupFailed(string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureCleanupFailed, step, ex_type, ex_message);
    }

    // ── Capture loop end ────────────────────────────────────────────────

    // One line per loop, whatever the exit: outcome is cancelled or failed,
    // reason as on ScreenCaptureStopped.
    [Event(EvtCaptureLoopEnded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "capture loop ended | outcome={0} | reason={1} | frames={2} | elapsed_ms={3}")]
    public void CaptureLoopEnded(string outcome, string reason, long frames, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLoopEnded, outcome, reason, frames, elapsed_ms);
    }

    [Event(EvtCaptureLoopCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture loop crashed and is stopping | hr=0x{0:X8} | ex_type={1} | ex_message={2}")]
    public void CaptureLoopCrashed(int hr, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLoopCrashed, hr, ex_type, ex_message);
    }

    [Event(EvtDeviceLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Graphics device lost, screen capture is stopping | hr=0x{0:X8}")]
    public void DeviceLost(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtDeviceLost, hr);
    }

    [Event(EvtFrameOwnershipRecoveryAbandoned,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture is stuck on an unreleased frame and is stopping | attempts={0} | max_attempts={1} | hr=0x{2:X8}")]
    public void FrameOwnershipRecoveryAbandoned(int attempts, int max_attempts, int hr)
    {
        if (IsEnabled()) WriteEvent(EvtFrameOwnershipRecoveryAbandoned, attempts, max_attempts, hr);
    }

    // ── Capture loop anomalies ──────────────────────────────────────────

    [Event(EvtAccessLostRecovering,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "access lost, recreating duplication")]
    public void AccessLostRecovering()
    {
        if (IsAmbientDetailEnabled(Keywords.Capture)) WriteEvent(EvtAccessLostRecovering);
    }

    // Raw attempt; AcquireFrameIncident opens once they repeat.
    [Event(EvtAcquireFrameFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "acquire next frame failed | hr=0x{0:X8} | backoff_ms={1}")]
    public void AcquireFrameFailed(int hr, int backoff_ms)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture)) WriteEvent(EvtAcquireFrameFailed, hr, backoff_ms);
    }

    [Event(EvtAcquireFrameIncident,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Screen capture cannot read frames and keeps retrying | hr=0x{0:X8} | failures={1}")]
    public void AcquireFrameIncident(int hr, int failures)
    {
        if (IsEnabled()) WriteEvent(EvtAcquireFrameIncident, hr, failures);
    }

    [Event(EvtAcquireFrameRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "Screen capture reads frames again | failures={0} | duration_ms={1}")]
    public void AcquireFrameRecovered(int failures, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAcquireFrameRecovered, failures, duration_ms);
    }

    [Event(EvtTextureQueryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "texture query failed | ex_type={0} | ex_message={1}")]
    public void TextureQueryFailedDetail(string ex_type, string ex_message)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture))
            WriteEvent(EvtTextureQueryFailedDetail, ex_type, ex_message);
    }

    [Event(EvtFrameConsumerThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame consumer threw | ex_type={0} | ex_message={1}")]
    public void FrameConsumerThrewDetail(string ex_type, string ex_message)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture))
            WriteEvent(EvtFrameConsumerThrewDetail, ex_type, ex_message);
    }

    // handler names the event whose subscriber threw (stopped,
    // format_changed). Rare by construction: per-frame handlers report
    // through FrameConsumerThrewDetail instead.
    [Event(EvtSubscriberThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A screen capture event handler failed | handler={0} | ex_type={1} | ex_message={2}")]
    public void SubscriberThrew(string handler, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSubscriberThrew, handler, ex_type, ex_message);
    }

    [Event(EvtReleaseFrameNonZero,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "release frame failed, ignored | hr=0x{0:X8}")]
    public void ReleaseFrameNonZero(int hr)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture)) WriteEvent(EvtReleaseFrameNonZero, hr);
    }

    [Event(EvtSecureDesktopRecovering,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "secure desktop or session disconnect, recreating duplication | hr=0x{0:X8}")]
    public void SecureDesktopRecovering(int hr)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture)) WriteEvent(EvtSecureDesktopRecovering, hr);
    }

    // ── Duplication recreate resilience ─────────────────────────────────

    [Event(EvtDuplicationResizeDetected,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "resize detected on recreate | old={0}x{1} | new={2}x{3}")]
    public void DuplicationResizeDetected(int old_width, int old_height, int new_width, int new_height)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture))
            WriteEvent(EvtDuplicationResizeDetected, old_width, old_height, new_width, new_height);
    }

    [Event(EvtDuplicationRecreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "duplication recreated | attempt={0} | size={1}x{2}")]
    public void DuplicationRecreated(int attempt, int width, int height)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture))
            WriteEvent(EvtDuplicationRecreated, attempt, width, height);
    }

    // Opens the recreate incident on the second failed attempt, with the
    // cause of that attempt.
    [Event(EvtDuplicationRecreateAttemptFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Reopening the capture stream failed and will be retried | attempt={0} | hr=0x{1:X8} | ex_type={2} | ex_message={3}")]
    public void DuplicationRecreateAttemptFailed(int attempt, int hr, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateAttemptFailed, attempt, hr, ex_type, ex_message);
    }

    [Event(EvtDuplicationRecreateAttemptFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "duplication recreate failed | attempt={0} | ex_type={1} | ex_message={2}")]
    public void DuplicationRecreateAttemptFailedDetail(int attempt, string ex_type, string ex_message)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture))
            WriteEvent(EvtDuplicationRecreateAttemptFailedDetail, attempt, ex_type, ex_message);
    }

    [Event(EvtDuplicationRecreateRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Journal,
           Message = "Screen capture recovered")]
    public void DuplicationRecreateRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateRecovered);
    }

    [Event(EvtDuplicationRecreateEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "duplication recreate episode | outcome={0} | failures={1} | duration_ms={2}")]
    public void DuplicationRecreateEpisodeDetail(string outcome, int failures, long duration_ms)
    {
        if (!OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture)) return;
        WriteEvent(EvtDuplicationRecreateEpisodeDetail, outcome, failures, duration_ms);
    }

    [Event(EvtDuplicationRecreateAbandoned,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Screen capture could not be reopened and is stopping | attempts={0} | hr=0x{1:X8} | ex_type={2} | ex_message={3}")]
    public void DuplicationRecreateAbandoned(int attempts, int hr, string ex_type, string ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtDuplicationRecreateAbandoned, attempts, hr, ex_type, ex_message);
    }

    [Event(EvtCaptureFormatRenegotiated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Capture surface renegotiated after a display change")]
    public void CaptureFormatRenegotiated()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureFormatRenegotiated);
    }

    [Event(EvtCaptureFormatRenegotiatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "renegotiate | old_format={0} | new_format={1} | hdr={2} | peak_lum={3:F0} | size={4}x{5} | attempt={6}")]
    public void CaptureFormatRenegotiatedDetail(string old_format, string new_format, string hdr_state, double peak_lum, int width, int height, int attempt)
    {
        if (IsAmbientDetailEnabled(Keywords.Capture | Keywords.Lifecycle))
            WriteEvent(EvtCaptureFormatRenegotiatedDetail, old_format, new_format, hdr_state, peak_lum, width, height, attempt);
    }

    // A failed probe falls back to SDR: an HDR desktop is then captured
    // through the SDR format list and its highlights clip.
    [Event(EvtHdrDetectionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "HDR detection failed, capture assumes SDR | hr=0x{0:X8} | ex_type={1} | ex_message={2}")]
    public void HdrDetectionFailed(int hr, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtHdrDetectionFailed, hr, ex_type, ex_message);
    }

    // ── FrameSampler ────────────────────────────────────────────────────

    [Event(EvtSamplerInitialized,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "sampler init | grid={0}x{1} | mip={2} | tone_map={3} | peak_lum={4:F0}")]
    public void SamplerInitialized(int cols, int rows, int target_mip, string tone_map, double peak_lum)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerInitialized, cols, rows, target_mip, tone_map, peak_lum);
    }

    // Per-frame sampler failures stay Verbose: the consumer aggregates them
    // into its own frame-processing incident and reads the last cause from
    // FrameSampler.LastFailure.
    [Event(EvtSamplerMapFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sampler map failed | hr=0x{0:X8}")]
    public void SamplerMapFailedDetail(int hr)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSamplerMapFailedDetail, hr);
    }

    [Event(EvtSamplerProcessFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sampler process failed | ex_type={0} | ex_message={1}")]
    public void SamplerProcessFailedDetail(string ex_type, string ex_message)
    {
        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSamplerProcessFailedDetail, ex_type, ex_message);
    }

    // ── Heartbeat — rolling capture telemetry ──────────────────────────
    //
    // One line per ScreenCaptureService heartbeat window, emitted from every
    // loop iteration so a static screen still reports acquired=0 instead of
    // falling silent. Acquire latency covers the AcquireNextFrame →
    // ReleaseFrame round-trip; sample latency covers the FrameArrived
    // handler. Microseconds because the acquire path is often
    // sub-millisecond and _ms would round its jitter away.
    //
    // Callers check the Verbose | Heartbeat gate before collecting samples,
    // so the loop allocates nothing when no listener is attached.
    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "capture heartbeat | period_ms={0} | acquired={1} | dropped={2} | acquire_p50_us={3} | acquire_p95_us={4} | sample_p50_us={5} | sample_p95_us={6}")]
    public void Heartbeat(int period_ms, int frames_acquired, int frames_dropped,
        long p50_acquire_us, long p95_acquire_us,
        long p50_sample_us, long p95_sample_us)
    {
        if (!OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtHeartbeat, period_ms, frames_acquired, frames_dropped,
            p50_acquire_us, p95_acquire_us, p50_sample_us, p95_sample_us);
    }
}
