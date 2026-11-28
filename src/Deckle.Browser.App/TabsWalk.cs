using System.Diagnostics;
using System.Globalization;
using Deckle.Browser.Views;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// The tabs part of the Browser self-check, on local pages only: nine small
// HTML files written into the output folder, each with its own ground colour,
// title and icon (a data: address inside the page, so no site is reached).
// Page 9 holds a link with target="_blank" to page 1.
//
// What it does, through the window's public methods:
//   1. pins pages 1 to 8 (each opened as a tab, then pinned), selects each
//      once, opens page 9 as a tab, and measures the private memory of the
//      web view processes with the eight awake, then with the eight suspended;
//   2. wakes three pinned sites and times how long until the page answers;
//   3. photographs the window (tabs) and its rail (tabs-rail);
//   4. saves the session and loads it back: it must hold the eight pinned and
//      the tab, in order;
//   5. runs the link of page 9 by script, which is not a person's click: no
//      tab may open.
//
// Figures and checks go in measures.txt beside the report; a check that fails
// stops the run with its words.
internal static class TabsWalk
{
    private const int PageCount = 9;

    private static readonly string[] Colours =
        ["#c0392b", "#2980b9", "#27ae60", "#8e44ad", "#d35400", "#16a085", "#2c3e50", "#b7950b", "#7f8c8d"];

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser, bool captureTabs, bool captureRail, bool loaded)
    {
        string outputFolder = context.OutputFolder;
        string measures = Path.Combine(outputFolder, "measures.txt");
        var failures = new List<string>();
        Uri[] pages = WritePages(Path.Combine(outputFolder, "pages"));
        Guid firstTab = browser.Session.Tabs[0].Id;

        double lastMegabytes = 0;
        async Task<string> Measure(string label)
        {
            (int processes, long bytes) = await browser.MeasureWebViewMemoryAsync();
            double megabytes = bytes / (1024.0 * 1024.0);
            lastMegabytes = megabytes;
            return string.Create(CultureInfo.InvariantCulture, $"{label}: {megabytes:0.0} MB in {processes} processes");
        }

        Task Note(string line) => File.AppendAllTextAsync(measures, line + Environment.NewLine);

        // 1. Eight pinned sites and a tab. Suspension is held off while they
        // load, so the awake figure is the awake one.
        browser.SuspendsHidden = false;
        var pinned = new List<Guid>();
        for (int i = 0; i < 8; i++)
        {
            Guid id = await browser.OpenTabAsync(pages[i]);
            browser.PinTab(id);
            pinned.Add(id);
        }
        browser.CloseTab(firstTab);
        foreach (Guid id in pinned) await browser.SelectAsync(id);
        Guid tab = await browser.OpenTabAsync(pages[8]);
        await Task.Delay(1500);
        await Note(await Measure("8 loaded pinned + 1 tab, none suspended"));
        context.Measures.Pinned8LoadedMb = lastMegabytes;

        browser.SuspendsHidden = true;
        await browser.SuspendHiddenAsync();
        await Task.Delay(3000);
        string suspended = await Measure("8 suspended pinned + 1 tab");
        context.Measures.Pinned8SuspendedMb = lastMegabytes;
        await Note($"{suspended} ({browser.SuspendedCount} web views suspended)");
        if (browser.SuspendedCount != 8) failures.Add($"suspension: {browser.SuspendedCount} web views suspended, 8 expected");
        context.Step("browser/tabs measured");

        // 2. Wake time of a suspended pinned site, over three of them.
        var wakes = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            wakes.Add(await browser.WakeAsync(pinned[i]));
            await browser.SelectAsync(tab);
            await Task.Delay(2000);
        }
        context.Measures.WakeMs = wakes.Average();
        await Note(string.Create(CultureInfo.InvariantCulture,
            $"wake one suspended pinned: {wakes.Average():0} ms (average of {string.Join(", ", wakes.Select(w => w.ToString("0", CultureInfo.InvariantCulture)))})"));
        context.Step("browser/tabs woken");

        // 3. The window with the grid and the tabs, then in its rail.
        browser.SetPaneOpen(true);
        await Task.Delay(600);
        if (captureTabs)
        {
            context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                context.Run, browser, "browser", "tabs", "Pinned grid and tabs", loaded, 0));
        }
        if (captureRail)
        {
            browser.SetPaneOpen(false);
            await Task.Delay(600);
            context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                context.Run, browser, "browser", "tabs-rail", "Panel in its rail", loaded, 0));
            browser.SetPaneOpen(true);
            await Task.Delay(300);
        }
        context.Step("browser/tabs captured");

        // 4. The session, written and read back.
        browser.SaveSession();
        BrowserSession back = SessionStore.Load(browser.SessionPath);
        string[] pinnedBack = [.. back.Pinned.Select(i => FileOf(i.Url))];
        string[] tabsBack = [.. back.Tabs.Select(i => FileOf(i.Url))];
        string[] expectedPinned = [.. Enumerable.Range(1, 8).Select(n => $"page{n}.html")];
        bool restart = pinnedBack.SequenceEqual(expectedPinned) && tabsBack.SequenceEqual(["page9.html"]) && back.SelectedId is not null;
        await Note($"restart check (8 pinned and the tab, in order, selection kept): {(restart ? "pass" : "FAIL")}");
        if (!restart)
            failures.Add($"restart check: read back pinned [{string.Join(", ", pinnedBack)}] and tabs [{string.Join(", ", tabsBack)}]");

        // 5. A page that opens a window by itself (a script, no click) gets none.
        // ExecuteScriptAsync runs its script as if a person had acted (the link
        // click below was seen to count as a click: seen in the run of
        // 2026-11-28), so that click is only recorded. The check is a window
        // asked for by a timer that fires after the page's transient user
        // activation (about 5 s) has expired.
        int tabsBefore = browser.Session.Tabs.Count;
        await browser.SelectAsync(tab);
        await browser.ExecuteScriptAsync("document.querySelector('a').click()");
        await Task.Delay(1500);
        int clickOpened = browser.Session.Tabs.Count - tabsBefore;
        await Note($"link click by script (ExecuteScriptAsync counts as a click): {clickOpened} tab(s) opened");
        foreach (Guid extra in browser.Session.Tabs.Skip(tabsBefore).Select(t => t.Id).ToList()) browser.CloseTab(extra);
        await browser.SelectAsync(tab);

        await Task.Delay(6000);
        await browser.ExecuteScriptAsync("setTimeout(function () { window.open('page1.html'); }, 6000); 1");
        await Task.Delay(8000);
        bool refused = browser.Session.Tabs.Count == tabsBefore;
        await Note($"page-opened window without a click opens no tab: {(refused ? "pass" : "FAIL")}");
        if (!refused) failures.Add($"a page opened a tab without a click ({tabsBefore} before, {browser.Session.Tabs.Count} after)");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    // The file name a page address ends in; numbers and names only reach the report.
    private static string FileOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? Path.GetFileName(uri.LocalPath) : "";

    private static Uri[] WritePages(string folder)
    {
        Directory.CreateDirectory(folder);
        var uris = new Uri[PageCount];
        for (int i = 0; i < PageCount; i++)
        {
            string colour = Colours[i];
            string icon = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='32' height='32'%3E"
                + $"%3Ccircle cx='16' cy='16' r='14' fill='%23{colour[1..]}'/%3E%3C/svg%3E";
            string link = i == PageCount - 1
                ? "<p><a href=\"page1.html\" target=\"_blank\" style=\"color:#fff\">Open page 1 in a window</a></p>"
                : "";
            string html = "<!doctype html><html><head><meta charset=\"utf-8\">"
                + $"<title>Page {i + 1}</title><link rel=\"icon\" href=\"{icon}\"></head>"
                + $"<body style=\"margin:0;background:{colour};color:#fff;font-family:Segoe UI,sans-serif\">"
                + $"<h1 style=\"padding:48px\">Page {i + 1}</h1>{link}</body></html>";
            string path = Path.Combine(folder, $"page{i + 1}.html");
            File.WriteAllText(path, html);
            uris[i] = new Uri(path);
        }
        return uris;
    }
}
