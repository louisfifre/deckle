using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class DeckleSettingsSource
{
    // ── Settings module nav registry ────────────────────────────────────

    [Event(EvtSettingsModuleRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "settings module registered | id={0} | tag={1}")]
    public void SettingsModuleRegistered(string id, string tag)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsModuleRegistered, id, tag);
    }

    [Event(EvtSettingsModuleUnregistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "settings module unregistered | id={0}")]
    public void SettingsModuleUnregistered(string id)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsModuleUnregistered, id);
    }

    // ── Settings cross-page search index ────────────────────────────────

    [Event(EvtSearchEntrySkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search entry skipped | reason=header-unresolved | page={0} | key={1}")]
    public void SearchEntrySkipped(string page_tag, string label_key)
    {
        if (IsEnabled()) WriteEvent(EvtSearchEntrySkipped, page_tag, label_key);
    }

    // ── Settings cross-page search (TitleBar box) ───────────────────────

    [Event(EvtSearchExecuted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search executed | query_len={0} | hits={1}")]
    public void SearchExecuted(int query_len, int hits)
    {
        if (IsEnabled()) WriteEvent(EvtSearchExecuted, query_len, hits);
    }

    [Event(EvtSearchNavigated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigated from search")]
    public void SearchNavigated()
    {
        if (IsEnabled()) WriteEvent(EvtSearchNavigated);
    }

    [Event(EvtSearchNavigatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search navigation | page={0} | card={1}")]
    public void SearchNavigatedDetail(string page_tag, string card_tag)
    {
        if (IsEnabled()) WriteEvent(EvtSearchNavigatedDetail, page_tag, card_tag);
    }

    // ── Settings TitleBar layout & search presentation ───────────────────

    [Event(EvtTitleBarLayout,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "titlebar layout | bar={0} | zone={1} | box={2} | logs_x={3} | logs_w={4} | inset_r={5} | mode={6}")]
    public void TitleBarLayout(int bar_w, int zone_w, int box_w, int logs_x, int logs_w, int inset_r, string mode)
    {
        if (IsEnabled()) WriteEvent(EvtTitleBarLayout, bar_w, zone_w, box_w, logs_x, logs_w, inset_r, mode);
    }

    [Event(EvtSearchPresentationChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search presentation | from={0} | to={1} | zone={2}")]
    public void SearchPresentationChanged(string from, string to, int zone_w)
    {
        if (IsEnabled()) WriteEvent(EvtSearchPresentationChanged, from, to, zone_w);
    }

    [Event(EvtSearchFocusReleased,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "search focus released | via={0}")]
    public void SearchFocusReleased(string via)
    {
        if (IsEnabled()) WriteEvent(EvtSearchFocusReleased, via);
    }
}
