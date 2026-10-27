using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Summary page ──────────────────────────────────────────────────────────

    [Event(EvtSummaryShown,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup summary was shown")]
    public void SummaryShown()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShown);
    }

    [Event(EvtSummaryShownDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "success={0} | items={1}")]
    public void SummaryShownDetail(bool success, int items)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtSummaryShownDetail, success, items);
    }

    // The manual recovery for a failed download: the user picks a file or a
    // folder they fetched themselves. The source path is not logged, only
    // which item and how it ended.
    [Event(EvtItemImported,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A setup item was imported by hand")]
    public void ItemImported(string item_id, long size_bytes)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemImported, item_id, size_bytes);
    }

    // reason=sha256_mismatch, files_missing and load_check_failed are verdicts
    // on the picked content (ex_type empty); reason=threw carries the
    // exception.
    [Event(EvtItemImportFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Importing a setup item failed\nitem_id={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void ItemImportFailed(string item_id, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtItemImportFailed, item_id, reason, ex_type, ex_message);
    }

}
