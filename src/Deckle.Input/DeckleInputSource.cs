using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Input;

// Input module provider. Covers the Raw Input host lifecycle (dedicated
// thread + message-only window), Precision Touchpad device presence
// (Bluetooth arrivals and removals), the periodic contact-frame rollup,
// and the frame-telemetry recording sessions.
//
// Frames themselves are never logged — at report cadence they would drown
// the LogWindow. They go to the dedicated JSONL recorder; the EventSource
// carries the aggregated rollup and the lifecycle milestones.
[EventSource(Name = "Deckle-Input")]
public sealed class DeckleInputSource : DeckleEventSource
{
    public static readonly DeckleInputSource Log = new();

    private DeckleInputSource() { }

    internal static bool IsAutocorrectDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Autocorrect, Log, level, keywords);

    internal static bool IsInputActivityDetailEnabled(EventLevel level, EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Input, Log, level, keywords);

    // Host names carried by the events both input hosts share.
    internal const string TouchpadHost = "touchpad";
    internal const string KeyboardHost = "keyboard";

    // 1 retired.
    // 2 retired.
    // 3 retired.
    public const int EvtTouchpadDetected         = 4;
    public const int EvtTouchpadDetectedDetail   = 5;
    public const int EvtTouchpadAbsent           = 6;
    public const int EvtTouchpadArrived          = 7;
    public const int EvtTouchpadRemoved          = 8;
    // 9 retired.
    // 10 retired.
    public const int EvtFrameRollup              = 11;
    public const int EvtRecordingStarted         = 12;
    public const int EvtRecordingStartedDetail   = 13;
    // 14 retired.
    // 15 retired.
    // 16 retired.
    // 17 retired.
    // 18 retired.
    // 19 retired.
    // 20 retired.
    // 21 retired.
    // 22 retired.
    // 23 retired.
    public const int EvtKeyboardRollup           = 24;
    public const int EvtWheelRecordingStarted      = 25;
    public const int EvtWheelRecordingStartedDetail = 26;
    // 27 retired.
    // 28 retired.
    // 29 retired.
    // 30 retired.
    public const int EvtWheelDeviceObserved        = 31;
    // 32 retired.
    // 33 retired.
    // 34 retired.
    public const int EvtHostStarted                = 35;
    public const int EvtHostStartedDetail          = 36;
    public const int EvtHostStopped                = 37;
    public const int EvtHostStartFailed            = 38;
    public const int EvtKeyboardHostStarted        = 39;
    public const int EvtKeyboardHostStartedDetail  = 40;
    public const int EvtKeyboardHostStopped        = 41;
    public const int EvtKeyboardHostStartFailed    = 42;
    public const int EvtInputPumpFailed            = 43;
    public const int EvtHostStopTimedOut           = 44;
    public const int EvtParserCreateFailed         = 45;
    public const int EvtMouseHookFailed            = 46;
    public const int EvtMouseHookSilenceStarted    = 47;
    public const int EvtMouseHookSilenceEnded      = 48;
    public const int EvtInputRelayDegraded         = 49;
    public const int EvtInputRelayRecovered        = 50;
    public const int EvtRecordingStopped           = 51;
    public const int EvtRecordingStoppedDetail     = 52;
    public const int EvtRecordingFailed            = 53;
    public const int EvtWheelRecordingStopped      = 54;
    public const int EvtWheelRecordingStoppedDetail = 55;
    public const int EvtWheelRecordingFailed       = 56;
    public const int EvtRecordingSizeUnavailable   = 57;
    public const int EvtTouchpadEnumerationFailed  = 58;
    public const int EvtSettingsLoadWarning = 59;
    public const int EvtSettingsLoadError = 60;

    // ── Raw input host lifecycle ─────────────────────────────────────────

    // Emitted once the boot probe has run, so the run log states how
    // many touchpads the host found without any verbosity turned on.
    [Event(EvtHostStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Raw input host started | touchpads={0}")]
    public void HostStarted(int touchpads)
    {
        if (IsEnabled()) WriteEvent(EvtHostStarted, touchpads);
    }

    [Event(EvtHostStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "raw input host window | hwnd={0} | thread_id={1}")]
    public void HostStartedDetail(long hwnd, int thread_id)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartedDetail, hwnd, thread_id);
    }

    // outcome is completed for a clean quit; failed when the pump died on its
    // own or did not quit in time, with reason get_message_failed, exception
    // or stop_timed_out.
    [Event(EvtHostStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Raw input host stopped | outcome={0} | reason={1}")]
    public void HostStopped(string outcome, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtHostStopped, outcome, reason);
    }

    [Event(EvtHostStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Raw input host failed to start | ex_type={0} | ex_message={1} | win32_error={2}")]
    public void HostStartFailed(string ex_type, string ex_message, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHostStartFailed, ex_type, ex_message, win32_error);
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

    // The boot probe could not list the HID devices at all, so "touchpads=0" in
    // the host's start line means "unknown" and not "none". step is count or list;
    // win32_error is the last error of the failing call.
    [Event(EvtTouchpadEnumerationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The touchpad probe could not list the input devices | step={0} | win32_error={1}")]
    public void TouchpadEnumerationFailed(string step, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtTouchpadEnumerationFailed, step, win32_error);
    }

    // reason is one of TouchpadParser's snake_case failure words.
    [Event(EvtParserCreateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Touchpad parser could not be created | reason={0}")]
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
        if (IsInputActivityDetailEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtFrameRollup,
            count, rate_hz, max_gap_ms, max_tips, fragmented, orphans, flushes, scan_mismatches);
    }

    // ── Frame recording sessions ─────────────────────────────────────────

    [Event(EvtRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
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
        if (IsEnabled()) WriteEvent(EvtRecordingStartedDetail, PathRedaction.Redact(path));
    }

    // Closes every armed session, including one that never saw a frame
    // (frames=0) and one an I/O error ended (outcome=failed).
    [Event(EvtRecordingStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Frame recording stopped | outcome={0} | frames={1}")]
    public void RecordingStopped(string outcome, long frames)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStopped, outcome, frames);
    }

    [Event(EvtRecordingStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "frame recording closed | path={0} | duration_s={1} | bytes={2}")]
    public void RecordingStoppedDetail(string path, double duration_s, long bytes)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingStoppedDetail, PathRedaction.Redact(path), duration_s, bytes);
    }

    [Event(EvtRecordingFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Frame recording encountered an error | ex_type={0} | ex_message={1}")]
    public void RecordingFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingFailed, ex_type, PathRedaction.Redact(ex_message));
    }

    // The file size only decorates the closing line; a failed stat must not
    // fail the stop, but its cause is still kept.
    [Event(EvtRecordingSizeUnavailable,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "recording size unavailable | recording={0} | ex_type={1} | ex_message={2}")]
    public void RecordingSizeUnavailable(string recording, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingSizeUnavailable, recording, ex_type, PathRedaction.Redact(ex_message));
    }

    // ── Keyboard host lifecycle ──────────────────────────────────────────

    // mouse_hook=false means the host runs on Raw Input alone: precision
    // scrolling cannot intercept and synthesized touchpad clicks are unseen.
    [Event(EvtKeyboardHostStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Keyboard input host started | mouse_hook={0}")]
    public void KeyboardHostStarted(bool mouse_hook)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStarted, mouse_hook);
    }

    [Event(EvtKeyboardHostStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "keyboard input host window | hwnd={0} | thread_id={1}")]
    public void KeyboardHostStartedDetail(long hwnd, int thread_id)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStartedDetail, hwnd, thread_id);
    }

    [Event(EvtKeyboardHostStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Keyboard input host stopped | outcome={0} | reason={1}")]
    public void KeyboardHostStopped(string outcome, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStopped, outcome, reason);
    }

    [Event(EvtKeyboardHostStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Keyboard input host failed to start | ex_type={0} | ex_message={1} | win32_error={2}")]
    public void KeyboardHostStartFailed(string ex_type, string ex_message, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtKeyboardHostStartFailed, ex_type, ex_message, win32_error);
    }

    // ── Shared host failures ─────────────────────────────────────────────

    // host is touchpad or keyboard. reason is get_message_failed (win32_error
    // set) or exception (ex_type/ex_message set). The pump is gone: the host
    // observes nothing more until it is restarted.
    [Event(EvtInputPumpFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Input host message pump stopped unexpectedly | host={0} | reason={1} | win32_error={2} | ex_type={3} | ex_message={4}")]
    public void InputPumpFailed(string host, string reason, int win32_error, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtInputPumpFailed, host, reason, win32_error, ex_type, ex_message);
    }

    [Event(EvtHostStopTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Input host thread did not stop in time | host={0} | timeout_ms={1}")]
    public void HostStopTimedOut(string host, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtHostStopTimedOut, host, timeout_ms);
    }

    // ── Keyboard activity rollup (30 s aggregate while input flows) ──────
    // Never carries typed text: counters only.

    [Event(EvtKeyboardRollup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "keyboard activity | keys={0} | injected_filtered={1} | pointer_downs={2} | wheel={3} | focus_changes={4}")]
    public void KeyboardRollup(int keys, int injected_filtered, int pointer_downs, int wheel, int focus_changes)
    {
        if (!IsAutocorrectDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtKeyboardRollup, keys, injected_filtered, pointer_downs, wheel, focus_changes);
    }

    // ── Mouse hook health ────────────────────────────────────────────────

    [Event(EvtMouseHookFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Mouse wheel hook failed to start | win32_error={0}")]
    public void MouseHookFailed(int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtMouseHookFailed, win32_error);
    }

    // Windows removes a low-level hook without notice when it times out. The
    // only witness is Raw Input still reporting physical wheel detents that
    // the hook never pairs; unpaired counts them.
    [Event(EvtMouseHookSilenceStarted,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Mouse wheel hook stopped receiving input | reason={0} | unpaired={1}")]
    public void MouseHookSilenceStarted(string reason, int unpaired)
    {
        if (IsEnabled()) WriteEvent(EvtMouseHookSilenceStarted, reason, unpaired);
    }

    // reason is hook_wheel_seen or host_stopped.
    [Event(EvtMouseHookSilenceEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Mouse wheel hook receives input again | reason={0} | unpaired={1}")]
    public void MouseHookSilenceEnded(string reason, int unpaired)
    {
        if (IsEnabled()) WriteEvent(EvtMouseHookSilenceEnded, reason, unpaired);
    }

    // The relay from hook and timers to the pump lost work. reason is the last
    // cause seen: queue_full (a hook wheel was dropped), post_failed (a thread
    // message was refused) or timer_failed (wheel correlation fell back to an
    // immediate flush). occurrences counts every loss of the incident so far.
    [Event(EvtInputRelayDegraded,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Keyboard input host is losing input relays | reason={0} | win32_error={1} | occurrences={2}")]
    public void InputRelayDegraded(string reason, int win32_error, int occurrences)
    {
        if (IsEnabled()) WriteEvent(EvtInputRelayDegraded, reason, win32_error, occurrences);
    }

    // reason is relay_quiet or host_stopped.
    [Event(EvtInputRelayRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Keyboard input host relays input again | reason={0} | occurrences={1}")]
    public void InputRelayRecovered(string reason, int occurrences)
    {
        if (IsEnabled()) WriteEvent(EvtInputRelayRecovered, reason, occurrences);
    }

    // ── Wheel recording sessions ─────────────────────────────────────────
    // Wheel events themselves never reach the EventSource (they go to the
    // JSONL); only the lifecycle milestones and the per-device line do.

    [Event(EvtWheelRecordingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Wheel recording started")]
    public void WheelRecordingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtWheelRecordingStarted);
    }

    [Event(EvtWheelRecordingStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "wheel recording | path={0}")]
    public void WheelRecordingStartedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWheelRecordingStartedDetail, PathRedaction.Redact(path));
    }

    [Event(EvtWheelRecordingStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "Wheel recording stopped | outcome={0} | events={1}")]
    public void WheelRecordingStopped(string outcome, long events)
    {
        if (IsEnabled()) WriteEvent(EvtWheelRecordingStopped, outcome, events);
    }

    [Event(EvtWheelRecordingStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "wheel recording closed | path={0} | duration_s={1} | bytes={2}")]
    public void WheelRecordingStoppedDetail(string path, double duration_s, long bytes)
    {
        if (IsEnabled()) WriteEvent(EvtWheelRecordingStoppedDetail, PathRedaction.Redact(path), duration_s, bytes);
    }

    [Event(EvtWheelRecordingFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Wheel recording encountered an error | ex_type={0} | ex_message={1}")]
    public void WheelRecordingFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWheelRecordingFailed, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtWheelDeviceObserved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "wheel device | dev={0} | name={1} | vid=0x{2:X4} | pid=0x{3:X4}")]
    public void WheelDeviceObserved(int dev, string name, uint vendor_id, uint product_id)
    {
        if (IsEnabled()) WriteEvent(EvtWheelDeviceObserved, dev, name, vendor_id, product_id);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The mouse wheel settings need attention | {0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The mouse wheel settings could not be saved | {0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
