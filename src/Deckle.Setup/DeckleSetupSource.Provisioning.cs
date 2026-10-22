using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Install items (InstallingPage) ────────────────────────────────────────
    // The native runtime is one of these items ("native-runtime"); it has no
    // events of its own any more.

    [Event(EvtItemStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Installing a setup item | item_id={0} | size_bytes={1}")]
    public void ItemStarted(string item_id, long size_bytes)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemStarted, item_id, size_bytes);
    }

    [Event(EvtItemSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item skipped | item_id={0} | reason={1}")]
    public void ItemSkippedDetail(string item_id, string reason)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemSkippedDetail, item_id, reason);
    }

    [Event(EvtItemInstalled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item was installed")]
    public void ItemInstalled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalled);
    }

    [Event(EvtItemInstalledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item ok | id={0} | bytes={1} | dur_ms={2} | sha256={3}")]
    public void ItemInstalledDetail(string id, long bytes, long dur_ms, string sha256)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemInstalledDetail, id, bytes, dur_ms, sha256);
    }

    // reason=threw carries the caught exception; reason=provisioning_failed is
    // a failure the item reported itself, whose text sits in ex_message with
    // ex_type empty (the providers return a message, not an exception).
    [Event(EvtItemDownloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item failed to install | item_id={0} | reason={1} | ex_type={2} | ex_message={3} | duration_ms={4}")]
    public void ItemDownloadFailed(string item_id, string reason, string ex_type, string ex_message, long duration_ms)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemDownloadFailed, item_id, reason, ex_type, ex_message, duration_ms);
    }

    // A cancelled download is a normal end, journaled like its failed sibling.
    [Event(EvtItemCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "A setup item download was cancelled")]
    public void ItemCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelled);
    }

    [Event(EvtItemCancelledDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item cancelled | id={0}")]
    public void ItemCancelledDetail(string id)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemCancelledDetail, id);
    }

}
