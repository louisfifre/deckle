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

}
