using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── HuePairingService ───────────────────────────────────────────────

    [Event(EvtBridgeAutoRestoreFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge auto-restore at boot failed, the user will need to re-pair | ex_type={0} | ex_message={1}")]
    public void BridgeAutoRestoreFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeAutoRestoreFailed, ex_type, ex_message);
    }

    [Event(EvtBridgePairingStored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
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
           Message = "restore skipped | reason=no_persisted_identity")]
    public void BridgeRestoreSkipped()
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeRestoreSkipped);
    }

    // client_key_present = false means the Entertainment streaming key is
    // missing from the vault, so Ambient lighting falls back to REST pushes.
    [Event(EvtBridgeRestoredFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Bridge restored from settings | client_key_present={0}")]
    public void BridgeRestoredFromSettings(bool client_key_present)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeRestoredFromSettings, client_key_present);
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

    // reason = probe_failed | identity_mismatch: why the cached endpoint was
    // distrusted and a local discovery pass started.
    [Event(EvtBridgeEndpointRecoveryStarting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge endpoint recovery starting | reason={0} | ex_type={1} | ex_message={2}")]
    public void BridgeEndpointRecoveryStarting(string reason, string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeEndpointRecoveryStarting, reason, ex_type, ex_message);
    }

    // reason = identity_mismatch | exception.
    [Event(EvtBridgeCandidateRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge candidate rejected | candidate_ip={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void BridgeCandidateRejectedDetail(string candidate_ip, string reason, string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtBridgeCandidateRejectedDetail, candidate_ip, reason, ex_type, ex_message);
    }

    [Event(EvtBridgeEndpointRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
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

    // reason = not_found (no bridge advertised) | no_valid_candidate (none
    // accepted the stored credentials) | ambiguous (several did). ex_type /
    // ex_message name the probe failure that started the recovery.
    [Event(EvtBridgeEndpointRecoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge endpoint recovery failed | reason={0} | ex_type={1} | ex_message={2}")]
    public void BridgeEndpointRecoveryFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeEndpointRecoveryFailed, reason, ex_type, ex_message);
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
