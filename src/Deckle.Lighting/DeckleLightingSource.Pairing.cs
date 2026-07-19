using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
    // ── Pairing ─────────────────────────────────────────────────────────

    [Event(EvtPairingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing started — press the link button on the bridge")]
    public void PairingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtPairingStarted);
    }

    [Event(EvtPairingStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair start | bridge_ip={0} | timeout_sec={1} | devicetype={2}")]
    public void PairingStartedDetail(string bridge_ip, int timeout_sec, string devicetype)
    {
        if (IsEnabled()) WriteEvent(EvtPairingStartedDetail, bridge_ip, timeout_sec, devicetype);
    }

    [Event(EvtBridgePaired,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge paired")]
    public void BridgePaired()
    {
        if (IsEnabled()) WriteEvent(EvtBridgePaired);
    }

    // A …Detail already exists (the pair-result mirror with username), so this
    // milestone mirror is named …Detail2 per the Verbose/Info separation rule.
    [Event(EvtBridgePairedDetail2,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair milestone | bridge_id={0}")]
    public void BridgePairedDetail2(string bridge_id)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairedDetail2, bridge_id);
    }

    [Event(EvtBridgePairedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair result | bridge_id={0} | username={1} | clientkey=[redacted]")]
    public void BridgePairedDetail(string bridge_id, string username_head)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairedDetail, bridge_id, username_head);
    }

    [Event(EvtPairingWaiting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair waiting | error_type=101 | next_attempt_in_ms={0}")]
    public void PairingWaiting(int next_attempt_in_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPairingWaiting, next_attempt_in_ms);
    }

    [Event(EvtPairingRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing rejected by the bridge")]
    public void PairingRejected()
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejected);
    }

    [Event(EvtPairingRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair rejected | error_type={0} | description={1}")]
    public void PairingRejectedDetail(int error_type, string description)
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejectedDetail, error_type, description);
    }

    [Event(EvtPairingTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing timed out — the link button was not pressed in time")]
    public void PairingTimedOut()
    {
        if (IsEnabled()) WriteEvent(EvtPairingTimedOut);
    }

    [Event(EvtBridgeUnreachable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge unreachable during pairing")]
    public void BridgeUnreachable()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachable);
    }

    [Event(EvtBridgeUnreachableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge unreachable | ex_type={0} | message={1}")]
    public void BridgeUnreachableDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachableDetail, ex_type, message);
    }

    [Event(EvtPairingHttpError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing HTTP request failed")]
    public void PairingHttpError()
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpError);
    }

    [Event(EvtPairingHttpErrorDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair http error | http_status={0} | reason={1}")]
    public void PairingHttpErrorDetail(int http_status, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpErrorDetail, http_status, reason);
    }

}
