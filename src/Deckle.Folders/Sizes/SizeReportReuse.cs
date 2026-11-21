using Deckle.Core;

namespace Deckle.Folders;

// Whether the sizes tool can show a place from a report it already holds, or
// has to measure it (spec, Open 17: Refresh re-runs the current tool).
//
// Refresh always measures: it is the person asking for the disk as it is now,
// so neither the report on screen nor an older one is served in its place, and
// above all not a stopped or failed one as if the analysis had run. Coming back
// to the tool on the place it showed shows the report it left, finished or
// stopped, which says itself that it was stopped; a report that was still
// running when the person left, or that failed, is measured again. Any other
// move takes the place from the last analysis that ran to the end, when it
// holds it (SizeReport.ForFolder).
public static class SizeReportReuse
{
    public static SizeReport? For(string place, bool refresh, SizeReport? shown, SizeReport? lastComplete)
    {
        if (refresh) return null;
        if (shown is { State: SizeScanState.Completed or SizeScanState.Cancelled }
            && FolderPaths.Comparer.Equals(shown.Place, place))
            return shown;
        return lastComplete is { IsComplete: true } ? lastComplete.ForFolder(place) : null;
    }
}
