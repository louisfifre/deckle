using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
    // ── Pairing ─────────────────────────────────────────────────────────

    [Event(EvtPairingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing started, waiting for the bridge link button")]
    public void PairingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtPairingStarted);
    }

    [Event(EvtPairingStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge_ip={0} | timeout_sec={1} | devicetype={2}")]
    public void PairingStartedDetail(string bridge_ip, int timeout_sec, string devicetype)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtPairingStartedDetail, bridge_ip, timeout_sec, devicetype);
    }

    [Event(EvtBridgePaired,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Bridge paired")]
    public void BridgePaired()
    {
        if (IsEnabled()) WriteEvent(EvtBridgePaired);
    }

    [Event(EvtBridgePairedDetail2,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge_id={0}")]
    public void BridgePairedDetail2(string bridge_id)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgePairedDetail2, bridge_id);
    }


    // error_type stays a literal: 101 (link button not pressed) is the only
    // bridge error that keeps the poll going; every other one is rejected.
    [Event(EvtPairingWaiting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "next_attempt_in_ms={0}")]
    public void PairingWaiting(int next_attempt_in_ms)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtPairingWaiting, next_attempt_in_ms);
    }

    [Event(EvtPairingRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The bridge rejected pairing\nerror_type={0} | description={1}")]
    public void PairingRejected(int error_type, string description)
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejected, error_type, description);
    }

    [Event(EvtPairingTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing timed out before the link button was pressed\nattempts={0} | timeout_s={1}")]
    public void PairingTimedOut(int attempts, double timeout_s)
    {
        if (IsEnabled()) WriteEvent(EvtPairingTimedOut, attempts, timeout_s);
    }

    [Event(EvtBridgeUnreachable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The bridge was unreachable during pairing\nex_type={0} | ex_message={1} | win32_error={2}")]
    public void BridgeUnreachable(string ex_type, string ex_message, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachable, ex_type, ex_message, win32_error);
    }

    [Event(EvtPairingHttpError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The pairing request failed\nstatus={0}")]
    public void PairingHttpError(int status)
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpError, status);
    }
}
