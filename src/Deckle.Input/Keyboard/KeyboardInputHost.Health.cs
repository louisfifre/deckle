using System.Runtime.InteropServices;

namespace Deckle.Input;

// Health of the paths the host cannot otherwise see failing: the low-level
// hook that Windows can remove without notice, and the relay from the hook
// and timers to the pump (a full wheel queue, a refused thread message, a
// timer that could not be armed). Each is one incident: a Warning when it
// starts, an Informational line when it ends.
public sealed partial class KeyboardInputHost
{
    private const uint HeartbeatTimerId = 2;

    // The pump's clock for work no input triggers: closing a quiet relay
    // incident and flushing a rollup whose last event preceded the silence.
    private const uint HeartbeatTimerMs = 30_000;

    // A live hook pairs every physical detent within the 40 ms window, so a
    // streak this long without one hook wheel is not a timing accident.
    private const int HookSilenceThreshold = 16;

    private const double RelayQuietMs = 30_000;

    private readonly RepeatingIncident _hookSilence = new(HookSilenceThreshold, quietMs: 0);
    private readonly RepeatingIncident _relayIncident = new(threshold: 1, RelayQuietMs);

    // Input thread only: the hook callback runs on the thread that installed it.
    private int _hookQueueDrops;
    private int _timerFailures;
    private int _lastTimerError;

    // RequestDrain posts from any thread, so these two are shared.
    private int _postFailures;
    private int _lastPostError;

    private void NotePostFailure()
    {
        Volatile.Write(ref _lastPostError, Marshal.GetLastWin32Error());
        Interlocked.Increment(ref _postFailures);
    }

    private void NoteTimerFailure()
    {
        _lastTimerError = Marshal.GetLastWin32Error();
        _timerFailures++;
    }

    // Raw Input saw a physical detent that no hook wheel paired before it
    // expired from the correlation buffer.
    private void NoteUnpairedRawWheel(double nowMs)
    {
        if (_hookSilence.Record(nowMs))
            DeckleInputSource.Log.MouseHookSilenceStarted("raw_wheel_without_hook", _hookSilence.Occurrences);
    }

    private void NoteHookWheelSeen(double nowMs)
    {
        if (_hookSilence.Clear(nowMs, out int unpaired))
            DeckleInputSource.Log.MouseHookSilenceEnded("hook_wheel_seen", unpaired);
    }

    // Logging happens here, on the pump, never in the hook callback that
    // counted the loss: a low-level hook must return promptly.
    private void EvaluateRelayHealth(double nowMs)
    {
        int drops = _hookQueueDrops;
        int timers = _timerFailures;
        int posts = Interlocked.Exchange(ref _postFailures, 0);
        _hookQueueDrops = 0;
        _timerFailures = 0;

        bool opened = false;
        string reason = "";
        int win32Error = 0;
        if (drops > 0)
        {
            opened |= _relayIncident.Record(nowMs, drops);
            reason = "queue_full";
            win32Error = 0;
        }
        if (timers > 0)
        {
            opened |= _relayIncident.Record(nowMs, timers);
            reason = "timer_failed";
            win32Error = _lastTimerError;
        }
        if (posts > 0)
        {
            opened |= _relayIncident.Record(nowMs, posts);
            reason = "post_failed";
            win32Error = Volatile.Read(ref _lastPostError);
        }

        if (opened)
            DeckleInputSource.Log.InputRelayDegraded(reason, win32Error, _relayIncident.Occurrences);
        else if (_relayIncident.Clear(nowMs, out int total))
            DeckleInputSource.Log.InputRelayRecovered("relay_quiet", total);
    }

    private void StartHeartbeat()
    {
        if (RawInputInterop.SetTimer(_hwnd, HeartbeatTimerId, HeartbeatTimerMs, IntPtr.Zero) == 0)
            NoteTimerFailure();
    }

    private void OnHeartbeat()
    {
        double nowMs = RawInputHost.NowMs;
        EvaluateRelayHealth(nowMs);
        if (HasRollupActivity && IsKeyboardRollupEnabled())
            TrackRollup(nowMs);
    }

    // Incidents end with the host that observed them, so the journal never
    // holds a Warning without its end.
    private void CloseHealthIncidents()
    {
        if (_hookSilence.Close(out int unpaired))
            DeckleInputSource.Log.MouseHookSilenceEnded("host_stopped", unpaired);

        _hookQueueDrops = 0;
        _timerFailures = 0;
        Interlocked.Exchange(ref _postFailures, 0);
        if (_relayIncident.Close(out int total))
            DeckleInputSource.Log.InputRelayRecovered("host_stopped", total);
    }
}
