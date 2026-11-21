using System.Runtime.CompilerServices;

namespace Deckle.Folders.Views;

// What the self-check drives in the sizes tool that the window's own review
// steps do not reach: an analysis held midway (12.23), its Stop button, and
// whether the report shown is whole. The window keeps its tool views to
// itself, so each sizes view registers here under the window that hosts it,
// as MapReview does; the tables hold them weakly.
public static class SizesReview
{
    private static readonly ConditionalWeakTable<IFoldersToolHost, SizesView> Views = new();
    private static readonly ConditionalWeakTable<IFoldersToolHost, SizeScanGate> Gates = new();

    internal static void Register(IFoldersToolHost host, SizesView view) => Views.AddOrUpdate(host, view);

    // The gate the next analyses in this window wait at, or null: in the app
    // nothing ever sets one.
    internal static SizeScanGate? GateFor(IFoldersToolHost host) => Gates.TryGetValue(host, out SizeScanGate? gate) ? gate : null;

    // From now on, the analyses in this window set aside the folders named,
    // until Release.
    public static SizeScanGate Hold(FoldersWindow window, Func<string, bool> folders)
    {
        var gate = new SizeScanGate(folders);
        Gates.AddOrUpdate(window, gate);
        return gate;
    }

    // Opens the gate and lets the next analyses run free.
    public static void Release(FoldersWindow window)
    {
        if (Gates.TryGetValue(window, out SizeScanGate? gate)) gate.Open();
        Gates.Remove(window);
    }

    // Waits until the running analysis has measured every folder the gate
    // does not hold, and the list has followed it.
    public static async Task WaitUntilHeldAsync(FoldersWindow window, TimeSpan patience)
    {
        if (!Views.TryGetValue(window, out SizesView? view) || GateFor(window) is not { } gate)
            throw new InvalidOperationException("No sizes analysis is held in this window.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(view.ProgressForReview is { ChildFolders: > 0 } p && gate.Held > 0
                 && p.ChildFoldersMeasured + gate.Held == p.ChildFolders))
        {
            if (clock.Elapsed > patience) throw new TimeoutException("The sizes analysis did not reach its held folders.");
            await Task.Delay(50);
        }
        // Two of the view's ticks, so the rows show what is measured.
        await Task.Delay(500);
    }

    // Presses Stop, as a click does.
    public static void Stop(FoldersWindow window)
    {
        if (Views.TryGetValue(window, out SizesView? view)) view.StopForReview();
    }

    public static bool ShowsCompleteReport(FoldersWindow window) =>
        Views.TryGetValue(window, out SizesView? view) && view.ShowsCompleteReportForReview;
}
