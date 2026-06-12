using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input;

// Input module provider. Covers the Raw Input host lifecycle (dedicated
// thread + message-only window), Precision Touchpad device presence
// (Bluetooth arrivals and removals), the periodic contact-frame rollup,
// and the frame-telemetry recording sessions.
//
// Frames themselves are never logged — at report cadence (~100 Hz) they
// would drown the LogWindow ("the window carries steps, not frames").
// They go to the dedicated JSONL recorder; the EventSource carries the
// aggregated rollup and the lifecycle milestones.
[EventSource(Name = "Deckle-Input")]
public sealed class DeckleInputSource : DeckleEventSource
{
    public static readonly DeckleInputSource Log = new();

    private DeckleInputSource() { }

    public const int EvtHostStarted            = 1;
    public const int EvtHostStopped            = 2;
    public const int EvtHostStartFailed        = 3;
    public const int EvtTouchpadDetected       = 4;
    public const int EvtTouchpadDetectedDetail = 5;
    public const int EvtTouchpadAbsent         = 6;
    public const int EvtTouchpadArrived        = 7;
    public const int EvtTouchpadRemoved        = 8;
    public const int EvtRegistrationFailed     = 9;
    public const int EvtParserCreateFailed     = 10;
    public const int EvtFrameRollup            = 11;
    public const int EvtRecordingStarted       = 12;
    public const int EvtRecordingStartedDetail = 13;
    public const int EvtRecordingStopped       = 14;
    public const int EvtRecordingStoppedDetail = 15;
    public const int EvtRecordingFailed        = 16;
    public const int EvtKeyboardHostStarted     = 17;
    public const int EvtKeyboardHostStopped     = 18;
    public const int EvtKeyboardHostStartFailed = 19;
    public const int EvtKeyboardRollup          = 20;

    // ── Raw input host lifecycle ─────────────────────────────────────────

    [Event(EvtHostStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "raw input host started | hwnd={0} | thread={1}")]
    public void HostStarted(long hwnd, int thread_id)
    {
        if (IsEnabled()) WriteEvent(EvtHostStarted, hwnd, thread_id);
    }

    [Event(EvtHostStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "raw input host stopped")]
    public void HostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped);
    }

    [Event(EvtHostStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "raw input host start failed | error={0}: {1}")]
    public void HostStartFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartFailed, ex_type, message);
    }

    // ── Touchpad presence ────────────────────────────────────────────────

    [Event(EvtTouchpadDetected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Touchpad detected")]
    public void TouchpadDetected()
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadDetected);
    }

    [Event(EvtTouchpadDetectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "touchpad | name={0} | vid=0x{1:X4} | pid=0x{2:X4} | x=[{3},{4}] | y=[{5},{6}] | slots={7} | report_bytes={8}")]
    public void TouchpadDetectedDetail(
        string name, uint vendor_id, uint product_id,
        int x_min, int x_max, int y_min, int y_max,
        int contact_slots, int report_bytes)
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadDetectedDetail,
            name, vendor_id, product_id, x_min, x_max, y_min, y_max, contact_slots, report_bytes);
    }

    [Event(EvtTouchpadAbsent,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "No precision touchpad detected")]
    public void TouchpadAbsent()
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadAbsent);
    }

    [Event(EvtTouchpadArrived,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Touchpad connected")]
    public void TouchpadArrived()
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadArrived);
    }

    [Event(EvtTouchpadRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Touchpad disconnected")]
    public void TouchpadRemoved()
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadRemoved);
    }

    [Event(EvtRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "raw input registration failed | win32_error={0}")]
    public void RegistrationFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtRegistrationFailed, win32_error);
    }

    [Event(EvtParserCreateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "touchpad parser creation failed | reason={0}")]
    public void ParserCreateFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtParserCreateFailed, reason);
    }

    // ── Frame rollup (5 s aggregate while frames flow) ───────────────────

    [Event(EvtFrameRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "frames | count={0} | rate_hz={1} | max_gap_ms={2} | max_tips={3} | fragmented={4} | orphans={5} | flushes={6} | scan_mismatches={7}")]
    public void FrameRollup(
        int count, double rate_hz, double max_gap_ms, int max_tips,
        int fragmented, long orphans, long flushes, long scan_mismatches)
    {
        if (IsEnabled()) WriteEvent(EvtFrameRollup,
            count, rate_hz, max_gap_ms, max_tips, fragmented, orphans, flushes, scan_mismatches);
    }

    // ── Frame recording sessions ─────────────────────────────────────────

    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Frame recording started")]
    public void RecordingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStarted);
    }

    [Event(EvtRecordingStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame recording | path={0}")]
    public void RecordingStartedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStartedDetail, path);
    }

    [Event(EvtRecordingStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Frame recording stopped")]
    public void RecordingStopped()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStopped);
    }

    [Event(EvtRecordingStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame recording closed | path={0} | frames={1} | duration_sec={2} | bytes={3}")]
    public void RecordingStoppedDetail(string path, long frames, double duration_sec, long bytes)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStoppedDetail, path, frames, duration_sec, bytes);
    }

    [Event(EvtRecordingFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame recording failed | error={0}: {1}")]
    public void RecordingFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingFailed, ex_type, message);
    }

    // ── Keyboard host lifecycle ──────────────────────────────────────────
    // Second Raw Input host (keyboard + mouse buttons + focus signals) for
    // the autocorrect observation layer. Separate from the touchpad host's
    // own thread and window.

    [Event(EvtKeyboardHostStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "keyboard input host started | hwnd={0} | thread={1}")]
    public void KeyboardHostStarted(long hwnd, int thread_id)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStarted, hwnd, thread_id);
    }

    [Event(EvtKeyboardHostStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "keyboard input host stopped")]
    public void KeyboardHostStopped()
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStopped);
    }

    [Event(EvtKeyboardHostStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "keyboard input host start failed | error={0}: {1}")]
    public void KeyboardHostStartFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStartFailed, ex_type, message);
    }

    // ── Keyboard activity rollup (30 s aggregate while input flows) ──────
    // Never carries typed text. Counters only — key transitions seen,
    // injected events filtered out, pointer button-downs, focus changes.

    [Event(EvtKeyboardRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "keyboard activity | keys={0} | injected_filtered={1} | pointer_downs={2} | focus_changes={3}")]
    public void KeyboardRollup(int keys, int injected_filtered, int pointer_downs, int focus_changes)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardRollup, keys, injected_filtered, pointer_downs, focus_changes);
    }
}
