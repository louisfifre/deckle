using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using Deckle.Browser.Views;
using Deckle.Contributions;
using Deckle.Hosting.SelfCheck;
using Deckle.Vision;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// What the self-check hands the Browser walk: the contract's three members,
// plus the run's report and capture and the host that builds the window.
internal sealed class BrowserWalkContext(
    ContributionHost host,
    SelfCheckReport report,
    InterfaceCaptureRun run,
    string outputFolder,
    string? targetView,
    Action<string> step) : IContributionWalkContext
{
    public ContributionHost Host { get; } = host;
    public SelfCheckReport Report { get; } = report;
    public InterfaceCaptureRun Run { get; } = run;
    public string OutputFolder { get; } = outputFolder;
    public string? TargetView { get; } = targetView;

    // What the whole walk measures, appended to the history at its end.
    public WalkMeasures Measures { get; } = new();

    public void Step(string state) => step(state);

    // The walk of this process reaches its context through the contract.
    public static BrowserWalkContext From(IContributionWalkContext context) =>
        (BrowserWalkContext)context;
}

// The self-check's walk of the Browser window: opened off screen, it
// navigates to a local page (a file this walk writes, never a real site),
// waits for the navigation to complete, then photographs it and measures the
// web view's memory.
//
//   window        the window as DWM composed it, panel open
//   window-rail   the same with the panel in its rail
//   page-xaml     the window's XAML content rendered by the framework
//   page-preview  the web view's own drawing (CapturePreviewAsync)
//   tabs          the window with eight pinned sites and a tab, panel open
//   tabs-rail     the same with the panel in its rail
//   permission    a site's question for the location, open
//   downloads     the downloads flyout, empty
//   adblock       a local page whose advertisement the blocker stopped
//
// The gestures walk (GesturesWalk.cs) presses the window's gestures through
// their controls and times each; at the end of a full run the measures of every
// walk are appended as one line to the history (WalkMeasures.cs).
//
// The tabs walk (TabsWalk.cs) then measures what eight suspended pinned sites
// cost, how long one takes to wake, that the session survives a restart and
// that a page cannot open a window by itself.
//
// The memory figure goes in measures.txt beside the report and in one Verbose
// log event: a count and a number, no address.
internal static class BrowserSelfCheck
{
    public static readonly string[] Slugs = ["window", "window-rail", "page-xaml", "page-preview", "tabs", "tabs-rail", "permission", "downloads", "adblock"];

    private const string PageHtml =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Self-check page</title></head>"
        + "<body style=\"margin:0;background:#2b7a78;color:#ffffff;font-family:Segoe UI,sans-serif\">"
        + "<h1 style=\"margin:0;padding:48px\">Deckle Browser self-check</h1>"
        + "<p style=\"padding:0 48px\">A local page: a coloured ground and a heading.</p>"
        + "</body></html>";

    public static async Task WalkAsync(BrowserWalkContext context)
    {
        SelfCheckReport report = context.Report;
        InterfaceCaptureRun run = context.Run;
        string outputFolder = context.OutputFolder;
        string captures = Path.Combine(outputFolder, "captures");
        void Step(string name) => context.Step(name);

        string? only = context.TargetView;
        if (only is not null && !Slugs.Contains(only))
        {
            report.Views = [.. Slugs];
            return;
        }
        bool Wants(string slug) => only is null || only == slug;

        // The blocker's engine is built from one local rule, so the run reaches
        // no list server; the probe by hand (AdblockProbe) is the one run that
        // lets the real lists load. Pinned before the window exists, which
        // would otherwise start loading them.
        string? probeUrl = AdblockProbe.Url;
        if (probeUrl is null)
        {
            var build = Stopwatch.StartNew();
            Deckle.Browser.Adblock.AdblockEngine local = AdblockWalk.LocalEngine();
            context.Measures.EngineBuildMs = build.Elapsed.TotalMilliseconds;
            context.Measures.EngineRules = AdblockWalk.LocalRule.Split((char)10, StringSplitOptions.RemoveEmptyEntries).Length;
            context.Measures.EngineSource = "local-rule";
            BrowserWindow.UseAdblockEngine(local);
        }

        var browser = (BrowserWindow)context.Host.Ensure("browser");
        OffscreenWindow.ShowInactive(browser);
        bool loaded = await WaitUntilLoadedAsync(browser);
        using (Process self = Process.GetCurrentProcess())
            context.Measures.LaunchToWindowReadyMs = (DateTime.Now - self.StartTime).TotalMilliseconds;

        if (probeUrl is not null)
        {
            // Only the probe: the other walks expect the local rule's engine.
            Step("browser/probe");
            await AdblockProbe.RunAsync(context, browser, probeUrl, loaded);
            return;
        }

        // A local page, in a folder under the output folder.
        string pages = Path.Combine(outputFolder, "pages");
        Directory.CreateDirectory(pages);
        string file = Path.Combine(pages, "index.html");
        await File.WriteAllTextAsync(file, PageHtml);

        var clock = Stopwatch.StartNew();
        bool navigated = await browser.NavigateAsync(new Uri(file));
        long loadMs = clock.ElapsedMilliseconds;
        context.Measures.LocalPageMs = clock.Elapsed.TotalMilliseconds;
        // The page reaches the screen on the frames after its completion.
        await Task.Delay(600);
        Step($"browser (navigated={navigated})");

        if (Wants("window"))
        {
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, browser, "browser", "window", "Panel open", loaded && navigated, loadMs));
            Step("browser/window done");
        }

        if (Wants("window-rail"))
        {
            browser.SetPaneOpen(false);
            await Task.Delay(600);
            report.Surfaces.Add(await CaptureWindowFrameAsync(
                run, browser, "browser", "window-rail", "Panel in its rail", loaded && navigated, loadMs));
            browser.SetPaneOpen(true);
            await Task.Delay(300);
            Step("browser/window-rail done");
        }

        if (Wants("page-xaml"))
        {
            SelfCheckSurface surface = new("browser", "page-xaml", "The window as the framework renders it",
                loaded, loadMs, null, 0, 0, "page-render");
            try
            {
                // The window's whole content, not CapturePageAsync: that one
                // looks for the first ScrollViewer under the element, which
                // here is the NavigationView's own pane scroller, and renders
                // that instead of the window.
                ElementImage.Result image = await ElementImage.CaptureWindowContentAsync(
                    browser, Path.Combine(captures, "browser-page-xaml.png"));
                string[] files = [.. image.Files.Select(f => Path.GetRelativePath(outputFolder, f))];
                surface = surface with
                {
                    Png = files[0],
                    Parts = files.Length > 1 ? files : null,
                    Width = image.Width,
                    Height = image.Height,
                };
            }
            catch (Exception ex)
            {
                surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
            }
            report.Surfaces.Add(surface);
            Step("browser/page-xaml done");
        }

        if (Wants("page-preview"))
        {
            string png = Path.Combine(captures, "browser-page-preview.png");
            SelfCheckSurface surface = new("browser", "page-preview", "The page as the web view draws it",
                loaded, loadMs, null, 0, 0, "webview-preview");
            try
            {
                Directory.CreateDirectory(captures);
                await using (var stream = new FileStream(png, FileMode.Create, FileAccess.ReadWrite))
                    await browser.CapturePagePreviewAsync(stream);
                (int width, int height) = PngSize(png);
                surface = surface with
                {
                    Png = Path.GetRelativePath(outputFolder, png),
                    Width = width,
                    Height = height,
                };
            }
            catch (Exception ex)
            {
                surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
            }
            report.Surfaces.Add(surface);
            Step("browser/page-preview done");
        }

        (int processes, long bytes) = await browser.MeasureWebViewMemoryAsync();
        double megabytes = bytes / (1024.0 * 1024.0);
        context.Measures.WebViewMb = megabytes;
        context.Measures.WebViewProcesses = processes;
        await File.WriteAllTextAsync(Path.Combine(outputFolder, "measures.txt"),
            string.Create(CultureInfo.InvariantCulture,
                $"web view private memory: {megabytes:0.0} MB in {processes} processes (local page loaded, navigation {loadMs} ms){Environment.NewLine}"));
        DeckleBrowserAppSource.Log.WebViewMeasured(processes, bytes);

        // The walks after this one may fail on a check; the figures they got
        // before it are kept all the same, with the outcome saying so.
        try
        {
            if (only is null || only is "tabs" or "tabs-rail")
            {
                Step("browser/tabs");
                await TabsWalk.RunAsync(context, browser, Wants("tabs"), Wants("tabs-rail"), loaded);
            }

            if (only is null or "permission" or "downloads")
            {
                Step("browser/security");
                await SecurityWalk.RunAsync(context, browser, Wants("permission"), Wants("downloads"), loaded);
            }

            if (only is null)
            {
                Step("browser/gestures");
                await GesturesWalk.RunAsync(context, browser);
            }

            if (only is null or "adblock")
            {
                Step("browser/adblock");
                await AdblockWalk.RunAsync(context, browser, Wants("adblock"), loaded);
            }

            context.Measures.Outcome = "passed";
        }
        finally
        {
            // A full run only: a run of one view has not measured the rest.
            if (only is null)
            {
                (long stalls, long worst) = browser.UiStallsForReview;
                context.Measures.UiStalls = stalls;
                context.Measures.WorstStallMs = worst;
                string history = context.Measures.AppendToHistory(outputFolder, DateTimeOffset.UtcNow);
                await File.AppendAllTextAsync(Path.Combine(outputFolder, "measures.txt"),
                    $"history line appended to {history}{Environment.NewLine}");
            }
        }
    }

    // The width and height an IHDR chunk gives, in the first 24 bytes of a PNG.
    private static (int Width, int Height) PngSize(string path)
    {
        Span<byte> head = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        return stream.Read(head) == 24
            ? (BinaryPrimitives.ReadInt32BigEndian(head[16..]), BinaryPrimitives.ReadInt32BigEndian(head[20..]))
            : (0, 0);
    }
}
