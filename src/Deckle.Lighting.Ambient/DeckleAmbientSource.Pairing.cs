using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── HuePairingService ───────────────────────────────────────────────

    [Event(EvtBridgeAutoRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge auto-restore at boot failed — the user will need to re-pair")]
    public void BridgeAutoRestoreFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeAutoRestoreFailed);
    }

    [Event(EvtBridgeAutoRestoreFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge auto-restore failed | ex_type={0} | ex_message={1}")]
    public void BridgeAutoRestoreFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeAutoRestoreFailedDetail, ex_type, ex_message);
    }

    [Event(EvtBridgePairingStored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge pairing stored")]
    public void BridgePairingStored()
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairingStored);
    }

    [Event(EvtBridgePairingStoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge pairing stored | bridge_id={0} | username_head={1}")]
    public void BridgePairingStoredDetail(string bridge_id, string username_head)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgePairingStoredDetail, bridge_id, username_head);
    }

    [Event(EvtBridgeRestoreSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restore | skipped — no persisted bridge identity")]
    public void BridgeRestoreSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoreSkipped);
    }

    [Event(EvtBridgeRestoredFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge restored from settings")]
    public void BridgeRestoredFromSettings()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoredFromSettings);
    }

    [Event(EvtBridgeRestoredFromSettingsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge restored from settings | bridge_id={0} | bridge_ip={1}")]
    public void BridgeRestoredFromSettingsDetail(string bridge_id, string bridge_ip)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeRestoredFromSettingsDetail, bridge_id, bridge_ip);
    }

    [Event(EvtBridgeForgotten,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge forgotten — persisted credentials cleared")]
    public void BridgeForgotten()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeForgotten);
    }

    [Event(EvtBridgeEndpointRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge endpoint recovered")]
    public void BridgeEndpointRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeEndpointRecovered);
    }

    [Event(EvtBridgeEndpointRecoveredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge endpoint recovered | bridge_id={0} | old_ip={1} | new_ip={2} | identity_migrated={3}")]
    public void BridgeEndpointRecoveredDetail(
        string bridge_id,
        string old_ip,
        string new_ip,
        bool identity_migrated)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeEndpointRecoveredDetail, bridge_id, old_ip, new_ip, identity_migrated);
    }

    [Event(EvtBridgeEndpointRecoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge endpoint recovery failed")]
    public void BridgeEndpointRecoveryFailed()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeEndpointRecoveryFailed);
    }

    [Event(EvtBridgeEndpointRecoveryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge endpoint recovery failed | bridge_id={0} | bridge_ip={1} | candidates={2} | valid={3} | cause={4}")]
    public void BridgeEndpointRecoveryFailedDetail(
        string bridge_id,
        string bridge_ip,
        int candidates,
        int valid,
        string cause)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeEndpointRecoveryFailedDetail, bridge_id, bridge_ip, candidates, valid, cause);
    }

}
