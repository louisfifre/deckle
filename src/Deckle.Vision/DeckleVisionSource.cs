using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// Vision module provider. Covers screen capture (DXGI Output Duplication) and
// frame sampling (FrameSampler: mip chain + staging + readback). Events cover
// capture session lifecycle, acquisition loop anomalies (ACCESS_LOST,
// DEVICE_REMOVED, timeout, backoff), and resilience (duplication recreate,
// resize detected).
//
// The source label in LogWindow remains "SCREEN" through "Deckle-Vision" →
// "VISION" derivation: slight display-side rename compared with legacy
// `LogSource.Screen`, expected for the migration.
[EventSource(Name = "Deckle-Vision")]
public sealed class DeckleVisionSource : DeckleEventSource
{
    public static readonly DeckleVisionSource Log = new();

    private DeckleVisionSource() { }

    public const int EvtScreenCaptureStarting           = 1;
    public const int EvtCaptureSessionConfigured        = 2;
    public const int EvtScreenCaptureStarted            = 3;
    public const int EvtCaptureStartFailed              = 4;
    public const int EvtCaptureStartFailedDetail        = 5;
    public const int EvtTargetMonitorResolved           = 6;
    public const int EvtMonitorNotFound                 = 7;
    public const int EvtCaptureLoopWaitFailed           = 8;
    public const int EvtScreenCaptureStopped            = 9;
    public const int EvtScreenCaptureStoppedDetail      = 10;
    public const int EvtAccessLostRecovering            = 11;
    public const int EvtDeviceLost                      = 12;
    public const int EvtAcquireFrameFailed              = 13;
    public const int EvtTextureQueryFailed              = 14;
    public const int EvtFrameConsumerThrew              = 15;
    public const int EvtReleaseFrameNonZero             = 16;
    public const int EvtDuplicationResizeDetected       = 18;
    public const int EvtDuplicationRecreated            = 19;
    public const int EvtDuplicationRecreateAttemptFailed = 20;
    public const int EvtSamplerInitialized              = 21;
    public const int EvtSamplerMapFailed                = 22;
    public const int EvtSamplerProcessFailed            = 23;
    public const int EvtSecureDesktopRecovering         = 24;
    public const int EvtHeartbeat                       = 25;
    public const int EvtCaptureFormatRenegotiated       = 26;
    public const int EvtCaptureFormatRenegotiatedDetail = 27;

    // ── Verbose mirrors (Verbose/Info separation) ───────────────────────
    // Fresh ids appended after the milestone sequence; each mirrors a
    // milestone whose IDs / k=v detail moved out of the Capital Info message.
    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event.
    public const int EvtMonitorNotFoundDetail           = 28;
    public const int EvtDeviceLostDetail                = 29;
    public const int EvtTextureQueryFailedDetail        = 30;
    public const int EvtFrameConsumerThrewDetail        = 31;
    public const int EvtDuplicationRecreateAttemptFailedDetail = 32;
    public const int EvtSamplerMapFailedDetail          = 33;
    public const int EvtSamplerProcessFailedDetail      = 34;

    // ── Capture session lifecycle ───────────────────────────────────────

    [Event(EvtScreenCaptureStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Screen capture starting")]
    public void ScreenCaptureStarting()
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStarting);
    }

    [Event(EvtCaptureSessionConfigured,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "start | hmon=0x{0:X} | size={1}x{2} | format={3} | hdr={4} | peak_lum={5:F0} | timeout_ms={6} | throttle_ms={7}")]
    public void CaptureSessionConfigured(long hmon, int width, int height, string format, string hdr_state, double peak_lum, int timeout_ms, int throttle_ms)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureSessionConfigured, hmon, width, height, format, hdr_state, peak_lum, timeout_ms, throttle_ms);
    }

    [Event(EvtScreenCaptureStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Screen capture started")]
    public void ScreenCaptureStarted()
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStarted);
    }

    [Event(EvtCaptureStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Screen capture failed to start")]
    public void CaptureStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureStartFailed);
    }

    [Event(EvtCaptureStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "start failed | hr=0x{0:X8} | type={1} | message={2}")]
    public void CaptureStartFailedDetail(int hr, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureStartFailedDetail, hr, ex_type, message);
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
           Message = "Monitor not found — falling back to primary. The display may be disconnected or its device name has changed.")]
    public void MonitorNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtMonitorNotFound);
    }

    [Event(EvtMonitorNotFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "monitor not found | requested={0}")]
    public void MonitorNotFoundDetail(string requested)
    {
        if (IsEnabled()) WriteEvent(EvtMonitorNotFoundDetail, requested);
    }

    [Event(EvtCaptureLoopWaitFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture loop wait threw — {0}: {1} (continuing shutdown)")]
    public void CaptureLoopWaitFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLoopWaitFailed, ex_type, message);
    }

    [Event(EvtScreenCaptureStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Screen capture stopped")]
    public void ScreenCaptureStopped()
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStopped);
    }

    [Event(EvtScreenCaptureStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "stop | frames={0} | duration_ms={1} | fps_avg={2:F1}")]
    public void ScreenCaptureStoppedDetail(long frames, long duration_ms, double fps_avg)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStoppedDetail, frames, duration_ms, fps_avg);
    }

    // ── Capture loop anomalies ──────────────────────────────────────────

    [Event(EvtAccessLostRecovering,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "AcquireNextFrame returned ACCESS_LOST — desktop switch / mode change, recreating duplication")]
    public void AccessLostRecovering()
    {
        if (IsEnabled()) WriteEvent(EvtAccessLostRecovering);
    }

    [Event(EvtDeviceLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Graphics device lost — the capture session is unrecoverable and is stopping")]
    public void DeviceLost()
    {
        if (IsEnabled()) WriteEvent(EvtDeviceLost);
    }

    [Event(EvtDeviceLostDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "device lost | hr=0x{0:X8}")]
    public void DeviceLostDetail(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtDeviceLostDetail, hr);
    }

    [Event(EvtAcquireFrameFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "AcquireNextFrame failed (hr=0x{0:X8}) — backing off {1} ms")]
    public void AcquireFrameFailed(int hr, int backoff_ms)
    {
        if (IsEnabled()) WriteEvent(EvtAcquireFrameFailed, hr, backoff_ms);
    }

    [Event(EvtTextureQueryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A captured frame could not be read — the frame was dropped and the session continues")]
    public void TextureQueryFailed()
    {
        if (IsEnabled()) WriteEvent(EvtTextureQueryFailed);
    }

    [Event(EvtTextureQueryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "texture query failed | ex_type={0} | message={1}")]
    public void TextureQueryFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtTextureQueryFailedDetail, ex_type, message);
    }

    [Event(EvtFrameConsumerThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A frame consumer failed — the frame was dropped and the session continues")]
    public void FrameConsumerThrew()
    {
        if (IsEnabled()) WriteEvent(EvtFrameConsumerThrew);
    }

    [Event(EvtFrameConsumerThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame consumer threw | ex_type={0} | message={1}")]
    public void FrameConsumerThrewDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtFrameConsumerThrewDetail, ex_type, message);
    }

    [Event(EvtReleaseFrameNonZero,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "ReleaseFrame hr=0x{0:X8} (ignored)")]
    public void ReleaseFrameNonZero(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtReleaseFrameNonZero, hr);
    }

    [Event(EvtSecureDesktopRecovering,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "AcquireNextFrame returned hr=0x{0:X8} (secure desktop or session disconnect) — recreating duplication")]
    public void SecureDesktopRecovering(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtSecureDesktopRecovering, hr);
    }

    // ── Duplication recreate resilience ─────────────────────────────────

    [Event(EvtDuplicationResizeDetected,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "resize detected on recreate | old={0}x{1} | new={2}x{3}")]
    public void DuplicationResizeDetected(int old_width, int old_height, int new_width, int new_height)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationResizeDetected, old_width, old_height, new_width, new_height);
    }

    [Event(EvtDuplicationRecreated,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "duplication recreated | attempt={0} | size={1}x{2}")]
    public void DuplicationRecreated(int attempt, int width, int height)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreated, attempt, width, height);
    }

    [Event(EvtDuplicationRecreateAttemptFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Reopening the capture stream failed and will be retried")]
    public void DuplicationRecreateAttemptFailed()
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateAttemptFailed);
    }

    [Event(EvtDuplicationRecreateAttemptFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "duplication recreate failed | attempt={0} | ex_type={1} | message={2}")]
    public void DuplicationRecreateAttemptFailedDetail(int attempt, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateAttemptFailedDetail, attempt, ex_type, message);
    }

    // Milestone — ungated by AmbientCaptureGate (Info always passes) so the
    // HDR↔SDR toggle is visible at test time even with LogAmbientCaptureActivity
    // off. This is the line that disambiguates the diagnosed silent freeze :
    // if it fires on the toggle, the recreate funnel saw the format flip and
    // signalled the consumer to rebuild. mode = "HDR" | "SDR".
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
        if (IsEnabled()) WriteEvent(EvtCaptureFormatRenegotiatedDetail, old_format, new_format, hdr_state, peak_lum, width, height, attempt);
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

    [Event(EvtSamplerMapFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A frame could not be read back for sampling")]
    public void SamplerMapFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerMapFailed);
    }

    [Event(EvtSamplerMapFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sampler map failed | hr=0x{0:X8}")]
    public void SamplerMapFailedDetail(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerMapFailedDetail, hr);
    }

    [Event(EvtSamplerProcessFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Frame sampling failed")]
    public void SamplerProcessFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerProcessFailed);
    }

    [Event(EvtSamplerProcessFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sampler process failed | ex_type={0} | message={1}")]
    public void SamplerProcessFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerProcessFailedDetail, ex_type, message);
    }

    // ── Heartbeat — rolling capture telemetry ──────────────────────────
    //
    // Periodic rollup of the DXGI Output Duplication loop. One line per
    // window (period_ms ≈ 1000) summarising frame throughput and intra-
    // tick latency distribution. Acquire latency covers the AcquireNext-
    // Frame → ReleaseFrame round-trip ; sample latency covers the
    // FrameSampler downscale + map path. Both in microseconds because
    // the acquire path is typically sub-millisecond — _us gives enough
    // resolution to surface jitter that _ms would round away.
    //
    // Strictly Verbose | Keywords.Heartbeat gated so the per-sample
    // collection itself collapses to a single IsEnabled check when no
    // listener is attached. Caller responsibility : check IsEnabled
    // before collecting samples (zero alloc on the rolling buffer when
    // off), and emit through this method which gates a second time
    // before WriteEvent.
    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "capture heartbeat | period_ms={0} | acquired={1} | dropped={2} | acquire_p50_us={3} acquire_p95_us={4} | sample_p50_us={5} sample_p95_us={6}")]
    public void Heartbeat(int period_ms, int frames_acquired, int frames_dropped,
        long p50_acquire_us, long p95_acquire_us,
        long p50_sample_us, long p95_sample_us)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtHeartbeat, period_ms, frames_acquired, frames_dropped,
            p50_acquire_us, p95_acquire_us, p50_sample_us, p95_sample_us);
    }
}
