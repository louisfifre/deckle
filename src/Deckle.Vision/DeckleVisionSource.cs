using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// Vision module provider. Couvre la capture écran (DXGI Output
// Duplication) et l'échantillonnage de frames (FrameSampler — mip
// chain + staging + readback). Les events couvrent le cycle de vie
// de la session de capture, les anomalies de la boucle d'acquisition
// (ACCESS_LOST, DEVICE_REMOVED, timeout, backoff), et la résilience
// (recreate de duplication, resize détecté).
//
// La source label dans la LogWindow reste "SCREEN" via la dérivation
// "Deckle.Vision" → "VISION" — léger renommage côté affichage par
// rapport au legacy `LogSource.Screen`, attendu pour la migration.
[EventSource(Name = "Deckle.Vision")]
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
    public const int EvtDuplicationRecreateFailed       = 17;
    public const int EvtDuplicationResizeDetected       = 18;
    public const int EvtDuplicationRecreated            = 19;
    public const int EvtDuplicationRecreateAttemptFailed = 20;
    public const int EvtSamplerInitialized              = 21;
    public const int EvtSamplerMapFailed                = 22;
    public const int EvtSamplerProcessFailed            = 23;

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
           Message = "Screen capture failed to start — {0}: {1}")]
    public void CaptureStartFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureStartFailed, ex_type, message);
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
           Message = "Monitor not found — requested={0}, falling back to primary. Display may be disconnected or the device name has changed.")]
    public void MonitorNotFound(string requested)
    {
        if (IsEnabled()) WriteEvent(EvtMonitorNotFound, requested);
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
           Message = "Screen capture stopped ({0} frames in {1:F1} s)")]
    public void ScreenCaptureStopped(long frames, double duration_sec)
    {
        if (IsEnabled()) WriteEvent(EvtScreenCaptureStopped, frames, duration_sec);
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
           Message = "D3D11 device lost (hr=0x{0:X8}) — capture session unrecoverable, stopping")]
    public void DeviceLost(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtDeviceLost, hr);
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
           Message = "Texture QI failed — {0}: {1} (frame dropped, session continues)")]
    public void TextureQueryFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtTextureQueryFailed, ex_type, message);
    }

    [Event(EvtFrameConsumerThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "FrameArrived consumer threw — {0}: {1} (frame dropped, session continues)")]
    public void FrameConsumerThrew(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtFrameConsumerThrew, ex_type, message);
    }

    [Event(EvtReleaseFrameNonZero,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "ReleaseFrame hr=0x{0:X8} (ignored)")]
    public void ReleaseFrameNonZero(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtReleaseFrameNonZero, hr);
    }

    // ── Duplication recreate resilience ─────────────────────────────────

    [Event(EvtDuplicationRecreateFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "Duplication recreate failed {0} times in a row — capture stopped, display may be disconnected")]
    public void DuplicationRecreateFailed(int max_attempts)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateFailed, max_attempts);
    }

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
           Message = "DuplicateOutput1 failed on recreate (attempt {0}/{1}) — {2}: {3}")]
    public void DuplicationRecreateAttemptFailed(int attempt, int max, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtDuplicationRecreateAttemptFailed, attempt, max, ex_type, message);
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
           Message = "sampler map fail | hr=0x{0:X8}")]
    public void SamplerMapFailed(int hr)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerMapFailed, hr);
    }

    [Event(EvtSamplerProcessFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "sampler process failed — {0}: {1}")]
    public void SamplerProcessFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerProcessFailed, ex_type, message);
    }
}
