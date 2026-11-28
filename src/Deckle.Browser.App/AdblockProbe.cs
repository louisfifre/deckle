using System.Diagnostics;
using System.Globalization;
using Deckle.Browser.Views;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// A one-off measure by hand, not a test: it runs only when the environment
// variable DECKLE_BROWSER_PROBE_URL is set (the merge gate never sets it). With
// the real lists downloaded, it opens that address, then for 30 s, every 2 s,
// asks the page whether an advertisement is showing, notes how many requests
// the blocker stopped, and photographs the page at 5 s and at 20 s.
//
// Ad showing is what YouTube's player marks itself with: a ".ad-showing" class.
// Figures go in probe.txt beside the report. The address given is the
// maintainer's own to read: it stays in that file, never in the log.
internal static class AdblockProbe
{
    public const string Variable = "DECKLE_BROWSER_PROBE_URL";

    public static string? Url => Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } v ? v : null;

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser, string url, bool loaded)
    {
        string probe = Path.Combine(context.OutputFolder, "probe.txt");
        Task Note(string line) => File.AppendAllTextAsync(probe, line + Environment.NewLine);

        var wait = Stopwatch.StartNew();
        Task<bool> ready = Deckle.Browser.Views.BrowserAdblock.Ready;
        bool completed = await Task.WhenAny(ready, Task.Delay(TimeSpan.FromMinutes(3))) == ready;
        await Note($"lists ready after {wait.Elapsed.TotalSeconds:0.0} s (completed={completed}, all fresh or fetched={(completed && ready.Result)})");
        (int rules, long buildMs) = BrowserAdblock.LastBuild;
        await Note($"engine built from the lists: {rules} rules in {buildMs} ms (last build)");
        foreach (string file in Directory.EnumerateFiles(BrowserAdblock.FiltersDirectory))
            await Note($"  {Path.GetFileName(file)}: {new FileInfo(file).Length} bytes");
        if (BrowserAdblock.Engine is { } engine)
        {
            Deckle.Browser.Adblock.CosmeticResources cosmetic = engine.Cosmetic(url);
            await Note($"cosmetic answer for the address: {cosmetic.HideSelectors.Count} selectors, injected script {cosmetic.InjectedScript.Length} characters");
        }
        await Note($"probe address: {url}");

        bool navigated = await browser.NavigateAsync(new Uri(url));
        await Note($"navigated={navigated}");

        var clock = Stopwatch.StartNew();
        int adSamples = 0, samples = 0;
        var captured = new HashSet<int>();
        for (int second = 0; second <= 30; second += 2)
        {
            TimeSpan until = TimeSpan.FromSeconds(second) - clock.Elapsed;
            if (until > TimeSpan.Zero) await Task.Delay(until);

            string showing;
            try
            {
                showing = await browser.ExecuteScriptAsync(
                    "(function(){var p=document.querySelector('#movie_player.ad-showing, .ad-showing');return p?'ad':'none';})()");
            }
            catch (Exception ex)
            {
                showing = ex.GetType().Name;
            }
            samples++;
            if (showing == "\"ad\"") adSamples++;
            string title = await Title(browser);
            await Note(string.Create(CultureInfo.InvariantCulture,
                $"t={clock.Elapsed.TotalSeconds:0.0}s ad-showing={showing} blocked={browser.BlockedCount} title={title}"));

            foreach (int at in new[] { 5, 20 })
            {
                if (second < at || !captured.Add(at)) continue;
                string png = Path.Combine(context.OutputFolder, "captures", $"browser-probe-{at}s.png");
                Directory.CreateDirectory(Path.GetDirectoryName(png)!);
                await using (var stream = new FileStream(png, FileMode.Create, FileAccess.ReadWrite))
                    await browser.CapturePagePreviewAsync(stream);
                context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                    context.Run, browser, "browser", $"probe-{at}s", $"The probed page at {at} s", loaded, 0));
                await Note($"capture at {at}s: {png}");
            }
        }

        await Note($"samples with an ad showing: {adSamples} of {samples}; blocked requests: {browser.BlockedCount}");
        context.Step("browser/probe done");
    }

    private static async Task<string> Title(BrowserWindow browser)
    {
        try { return await browser.ExecuteScriptAsync("document.title"); }
        catch (Exception) { return "?"; }
    }
}
