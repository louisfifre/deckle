using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Contributions;

// Covers a composition's registration, the start hooks of its contributions
// and the opens the host refused. A window's creation, placement and load
// are reported by the executable's own provider and DeckleWindowingSource.
[EventSource(Name = "Deckle-Contributions")]
public sealed class DeckleContributionsSource : DeckleEventSource
{
    public static readonly DeckleContributionsSource Log = new();

    private DeckleContributionsSource() { }

    public const int EvtContributionRegistered = 1;
    public const int EvtRegistrationRefused    = 2;
    public const int EvtWindowNotOpened        = 3;
    public const int EvtComposeFailed          = 4;

    // One line per contribution at every start, so it stays detail.
    [Event(EvtContributionRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "id={0} | windows={1} | walks={2}")]
    public void ContributionRegistered(string id, int windows, int walks)
    {
        if (IsEnabled()) WriteEvent(EvtContributionRegistered, id, windows, walks);
    }

    // The composition stops here: the exception follows and ends the start.
    [Event(EvtRegistrationRefused,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A contribution was refused, so the composition does not start\nid={0} | reason={1}")]
    public void RegistrationRefused(string id, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtRegistrationRefused, id, reason);
    }

    // Work that does not happen says why, tagged like a start. reason is a
    // value of WindowRefusals.
    [Event(EvtWindowNotOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A contribution window was not opened")]
    public void WindowNotOpened(string window, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtWindowNotOpened, window, reason);
    }

    // The contribution runs without the hooks it asked for; the host starts.
    [Event(EvtComposeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A contribution could not plant its start hooks, so it runs without them\nid={0} | ex_type={1}")]
    public void ComposeFailed(string id, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtComposeFailed, id, ex_type);
    }

    public static class RegistrationRefusals
    {
        public const string InvalidId = "invalid_id";
        public const string DuplicateContribution = "duplicate_contribution";
        public const string DescriptorIdMismatch = "descriptor_id_mismatch";
        public const string InvalidWindowName = "invalid_window_name";
        public const string DuplicateWindow = "duplicate_window";
        public const string ReservedWindowName = "reserved_window_name";
        public const string WalkWithoutWindow = "walk_without_window";
        public const string DependencyCycle = "dependency_cycle";
    }

    public static class WindowRefusals
    {
        public const string UnknownWindow = "unknown_window";
        public const string DeveloperModeOff = "developer_mode_off";
        public const string ModuleAbsent = "module_absent";
    }
}
