namespace Deckle.Vision;

// Closed vocabulary of the incidents a capture loop can leave open. It spells the
// `incident` field of CaptureIncidentAbandoned the same way wherever it is read.
public static class CaptureIncidentKinds
{
    // AcquireNextFrame kept failing (AcquireFrameIncident).
    public const string AcquireFailures = "acquire_failures";

    // The duplication could not be reopened yet (DuplicationRecreateAttemptFailed).
    public const string Recreate = "recreate";

    // The duplication kept being lost and reopened (CaptureAccessIncident).
    public const string AccessLoss = "access_loss";
}

// Closed vocabulary of why the duplication was lost, the `cause` field of
// CaptureAccessIncident.
public static class CaptureLossCauses
{
    // DXGI_ERROR_ACCESS_LOST: a mode change, a DWM switch, a fullscreen swap.
    public const string AccessLost = "access_lost";

    // Access denied or session disconnected: the secure desktop or an RDP switch.
    public const string SecureDesktop = "secure_desktop";
}

// The incidents of one capture loop. Each is a degradation that repeats, so it
// writes one Warning when it opens and one log line when it ends, and the
// loop's exit closes whatever is still open: an incident that nothing declared
// over reads as abandoned. Time comes in as a tick count so the rules can be
// exercised without waiting.
//
// Only the capture loop's worker thread calls it, so it holds no lock.
internal sealed class CaptureIncidents
{
    // A display that is lost this many times inside the window is flapping, not
    // changing mode once.
    internal const int AccessLossThreshold = 4;
    internal const int AccessLossWindowMs = 30_000;

    // The flapping ends once the display has stayed up this long.
    internal const int AccessQuietMs = 10_000;

    private readonly int _acquireThreshold;
    private readonly long _ticksPerSecond;

    private int _acquireFailures;
    private long _acquireStartTicks;
    private bool _acquireOpen;

    private int _lossCount;
    private long _lossWindowStartTicks;
    private long _lastLossTicks;
    private bool _lossOpen;

    private int _recreateAttempts;
    private long _recreateStartTicks;
    private bool _recreateStarted;
    private bool _recreateOpen;

    internal CaptureIncidents(int acquireFailureThreshold, long ticksPerSecond)
    {
        _acquireThreshold = acquireFailureThreshold;
        _ticksPerSecond = ticksPerSecond;
    }

    // ── Generic AcquireNextFrame failures ────────────────────────────────

    // The raw attempts stay Verbose in the loop; the threshold opens one Warning
    // with the last hr.
    internal void AcquireFailed(int hr, long nowTicks)
    {
        if (_acquireFailures == 0) _acquireStartTicks = nowTicks;
        _acquireFailures++;
        if (!_acquireOpen && _acquireFailures >= _acquireThreshold)
        {
            _acquireOpen = true;
            DeckleVisionSource.Log.AcquireFrameIncident(hr, _acquireFailures);
        }
    }

    // A readable frame, or a timeout on a static screen: the duplication answers.
    internal void AcquireReadable(long nowTicks)
    {
        if (_acquireFailures == 0) return;
        if (_acquireOpen)
        {
            DeckleVisionSource.Log.AcquireFrameRecovered(
                _acquireFailures, ElapsedMs(_acquireStartTicks, nowTicks));
        }
        ResetAcquire();
    }

    // ── A duplication that keeps being lost ──────────────────────────────

    // Each loss recreates the duplication and the loop goes on, so a display that
    // flaps shows nothing but Verbose lines unless the losses are counted.
    internal void AccessLost(string cause, long nowTicks)
    {
        if (!_lossOpen
            && (_lossCount == 0 || ElapsedMs(_lossWindowStartTicks, nowTicks) > AccessLossWindowMs))
        {
            _lossWindowStartTicks = nowTicks;
            _lossCount = 0;
        }

        _lossCount++;
        _lastLossTicks = nowTicks;

        if (!_lossOpen && _lossCount >= AccessLossThreshold)
        {
            _lossOpen = true;
            DeckleVisionSource.Log.CaptureAccessIncident(
                cause, _lossCount, ElapsedMs(_lossWindowStartTicks, nowTicks));
        }
    }

    // Called with every frame and every timeout: the incident ends once the
    // display has stayed up for AccessQuietMs.
    internal void Healthy(long nowTicks)
    {
        if (!_lossOpen) return;
        if (ElapsedMs(_lastLossTicks, nowTicks) < AccessQuietMs) return;

        DeckleVisionSource.Log.CaptureAccessRecovered(
            _lossCount, ElapsedMs(_lossWindowStartTicks, nowTicks));
        ResetLoss();
    }

    // ── Duplication recreate ─────────────────────────────────────────────

    internal bool RecreateOpen => _recreateOpen;

    // The clock starts at the first failed attempt, whether or not the incident
    // opens, so the line that ends it always has a duration.
    internal void RecreateFailed(int attempt, long nowTicks)
    {
        if (!_recreateStarted)
        {
            _recreateStarted = true;
            _recreateStartTicks = nowTicks;
        }

        _recreateAttempts = attempt;
    }

    // The caller has just written the Warning that opens the incident.
    internal void RecreateOpened() => _recreateOpen = true;

    internal long RecreateElapsedMs(long nowTicks)
        => _recreateStarted ? ElapsedMs(_recreateStartTicks, nowTicks) : 0;

    // The caller has just written the line that ends it, a recovery or a terminal
    // Error, so the loop's exit has nothing left to close.
    internal void RecreateEnded()
    {
        _recreateAttempts = 0;
        _recreateStartTicks = 0;
        _recreateStarted = false;
        _recreateOpen = false;
    }

    // ── Exit ─────────────────────────────────────────────────────────────

    // The loop is over (a stop, a device lost, a crash): every incident still open
    // gets its closing line, once.
    internal void AbandonOpen(long nowTicks)
    {
        if (_acquireOpen)
        {
            DeckleVisionSource.Log.CaptureIncidentAbandoned(
                CaptureIncidentKinds.AcquireFailures, _acquireFailures,
                ElapsedMs(_acquireStartTicks, nowTicks));
        }
        ResetAcquire();

        if (_lossOpen)
        {
            DeckleVisionSource.Log.CaptureIncidentAbandoned(
                CaptureIncidentKinds.AccessLoss, _lossCount,
                ElapsedMs(_lossWindowStartTicks, nowTicks));
        }
        ResetLoss();

        if (_recreateOpen)
        {
            DeckleVisionSource.Log.CaptureIncidentAbandoned(
                CaptureIncidentKinds.Recreate, _recreateAttempts,
                RecreateElapsedMs(nowTicks));
        }
        RecreateEnded();
    }

    private void ResetAcquire()
    {
        _acquireFailures = 0;
        _acquireStartTicks = 0;
        _acquireOpen = false;
    }

    private void ResetLoss()
    {
        _lossCount = 0;
        _lossWindowStartTicks = 0;
        _lastLossTicks = 0;
        _lossOpen = false;
    }

    private long ElapsedMs(long fromTicks, long toTicks)
        => (toTicks - fromTicks) * 1000L / _ticksPerSecond;
}
