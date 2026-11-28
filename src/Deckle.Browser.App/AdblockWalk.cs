using Deckle.Browser.Adblock;
using Deckle.Browser.Views;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// The ad blocker part of the Browser self-check, on local pages only. The
// engine in force is built from one local rule, ||ads.browser.test^, and not
// from the lists, so the run never reaches the network. A virtual host name
// (browser.test) is mapped to the walk's pages folder; the page asks for an
// image from ads.browser.test (not mapped: a request that is let through would
// fail to resolve, one the engine blocks never leaves the browser) and for a
// style sheet of its own host.
//
//   1. the rule blocks the image: the shown page's count reads 1, the image did
//      not load, and the style sheet did (the page reads it);
//   2. the shield's tooltip says "1 blocked on this page";
//   3. the window is photographed (slug "adblock").
internal static class AdblockWalk
{
    public const string LocalRule = "||ads.browser.test^\n";
    private const string Host = "browser.test";

    private const string Page =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>blocker</title>"
        + "<link rel=\"stylesheet\" href=\"/own.css\"></head>"
        + "<body class=\"own\"><h1 style=\"padding:48px;margin:0\">This page asks for an advertisement</h1>"
        + "<img id=\"ad\" src=\"https://ads.browser.test/ad.png\" width=\"300\" height=\"120\" alt=\"\">"
        + "</body></html>";

    private const string Css = "body.own { background: #1f6f4a; color: #ffffff; font-family: Segoe UI, sans-serif; }";

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser, bool capture, bool loaded)
    {
        string measures = Path.Combine(context.OutputFolder, "measures.txt");
        string pages = Path.Combine(context.OutputFolder, "pages");
        var failures = new List<string>();
        Task Note(string line) => File.AppendAllTextAsync(measures, line + Environment.NewLine);

        Directory.CreateDirectory(pages);
        await File.WriteAllTextAsync(Path.Combine(pages, "blocked.html"), Page);
        await File.WriteAllTextAsync(Path.Combine(pages, "own.css"), Css);
        browser.MapVirtualHost(Host, pages);

        bool navigated = await browser.NavigateAsync(new Uri($"https://{Host}/blocked.html"));
        await Task.Delay(1500);

        int blocked = browser.BlockedCount;
        string imageState = await browser.ExecuteScriptAsync("(function(){var i=document.getElementById('ad');return i.complete&&i.naturalWidth===0?'blocked':'loaded';})()");
        string background = await browser.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor");
        bool imageBlocked = imageState == "\"blocked\"";
        bool styleLoaded = background == "\"rgb(31, 111, 74)\"";
        await Note($"the rule ||ads.browser.test^ blocks the page's image (count {blocked}, expected 1): {(blocked == 1 ? "pass" : "FAIL")} (navigated={navigated})");
        await Note($"the image did not load: {(imageBlocked ? "pass" : "FAIL")}; the page's own style sheet did: {(styleLoaded ? "pass" : "FAIL")}");
        if (blocked != 1) failures.Add($"the blocked count read {blocked}, 1 expected");
        if (!imageBlocked) failures.Add($"the advertisement image state read {imageState}");
        if (!styleLoaded) failures.Add($"the page's own style sheet did not apply ({background})");

        string tooltip = browser.AdblockTooltipText;
        bool tooltipOk = tooltip.Contains('1') && tooltip.Contains("blocked", StringComparison.OrdinalIgnoreCase);
        await Note($"the shield says \"{tooltip}\": {(tooltipOk ? "pass" : "FAIL")}");
        if (!tooltipOk) failures.Add($"the shield's tooltip read \"{tooltip}\"");

        if (capture)
        {
            context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                context.Run, browser, "browser", "adblock", "A page whose advertisement the blocker stopped", loaded, 0));
        }
        context.Step("browser/adblock done");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    /// <summary>The engine of the walk: the local rule alone.</summary>
    public static AdblockEngine LocalEngine() => AdblockEngine.Create(LocalRule);
}
