using Deckle.Browser.Views;
using Deckle.Hosting.SelfCheck;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// The security part of the Browser self-check, on local pages only. A virtual
// host name (browser.test) is mapped to the walk's pages folder, so the pages
// load over https without a server and without reaching any site.
//
// What it does, through the window's public methods:
//   1. notes whether strict tracking prevention was in force;
//   2. loads a page that asks for the location on load: the question must
//      appear (capture "permission"); it is answered Block, the page must see
//      the refusal and the answer must be stored;
//   3. opens an http address of the mapped host: HTTPS first must move the tab
//      to https;
//   4. photographs the downloads flyout, empty ("downloads");
//   5. opens a private window on a local page: its web view must run in
//      private mode.
//
// A page over plain http showing "Not secure" in the field cannot be captured
// here without a server, so it is not.
//
// Checks and figures go in measures.txt; a check that fails stops the run with
// its words.
internal static class SecurityWalk
{
    private const string Host = "browser.test";
    private const string Origin = "https://browser.test";

    private const string GeolocationPage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>asking</title></head>"
        + "<body style=\"margin:0;background:#34495e;color:#fff;font-family:Segoe UI,sans-serif\">"
        + "<h1 style=\"padding:48px\">This page asks for the location</h1>"
        + "<script>navigator.geolocation.getCurrentPosition("
        + "function () { document.title = 'allowed'; },"
        + "function (e) { document.title = 'denied:' + e.code; });</script></body></html>";

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser, bool capturePermission, bool captureDownloads, bool loaded)
    {
        string measures = Path.Combine(context.OutputFolder, "measures.txt");
        string pages = Path.Combine(context.OutputFolder, "pages");
        var failures = new List<string>();
        Task Note(string line) => File.AppendAllTextAsync(measures, line + Environment.NewLine);

        Directory.CreateDirectory(pages);
        await File.WriteAllTextAsync(Path.Combine(pages, "geo.html"), GeolocationPage);
        browser.MapVirtualHost(Host, pages);

        // 1. Tracking prevention.
        await Note($"strict tracking prevention in force (read back): {(browser.StrictTrackingApplied ? "yes" : "NO")}");
        if (!browser.StrictTrackingApplied) failures.Add("strict tracking prevention was not in force");
        await Note($"web engine version: {browser.EngineVersion}");

        // 2. A permission question.
        Task<Guid> opening = browser.OpenTabAsync(new Uri($"{Origin}/geo.html"));
        bool asked = await browser.WaitForPermissionQuestionAsync(TimeSpan.FromSeconds(20));
        await Note($"a page asking for the location makes the question appear: {(asked ? "pass" : "FAIL")}");
        if (asked)
        {
            await Task.Delay(700);
            if (capturePermission)
            {
                context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                    context.Run, browser, "browser", "permission", "A site asks for the location", loaded, 0));
            }
            browser.AnswerPermission(PermissionDecision.Block);
        }
        else
        {
            failures.Add("no permission question appeared");
        }

        Guid tab = await opening;
        await Task.Delay(1000);
        string title = await browser.ExecuteScriptAsync("document.title");
        bool refused = title == "\"denied:1\"";
        PermissionDecision? stored = browser.StoredPermissionOf(Origin, PermissionKind.Geolocation);
        bool kept = stored == PermissionDecision.Block;
        await Note($"Block: the page saw the refusal {(refused ? "pass" : "FAIL")}, the answer is stored {(kept ? "pass" : "FAIL")}");
        if (asked && !refused) failures.Add($"after Block the page's title read {title}");
        if (asked && !kept) failures.Add($"the stored answer is {stored?.ToString() ?? "none"}, Block expected");
        context.Step("browser/permission done");

        // 3. HTTPS first, on the mapped host.
        Task<Guid> plain = browser.OpenTabAsync(new Uri($"http://{Host}/geo.html"));
        await plain;
        await Task.Delay(2000);
        string protocol = await browser.ExecuteScriptAsync("location.protocol");
        bool upgraded = protocol == "\"https:\"";
        await Note($"an http address is moved to https: {(upgraded ? "pass" : "FAIL")}");
        if (!upgraded) failures.Add($"after an http address the page's protocol read {protocol}");
        context.Step("browser/https-first done");

        // 4. The downloads flyout, empty.
        browser.CloseTab(tab);
        await Task.Delay(500);
        browser.ShowDownloads();
        await Task.Delay(800);
        if (captureDownloads)
        {
            context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                context.Run, browser, "browser", "downloads", "The downloads flyout, empty", loaded, 0));
        }
        browser.HideDownloads();
        context.Step("browser/downloads done");

        // 5. A private window.
        var priv = new BrowserWindow(true);
        OffscreenWindow.ShowInactive(priv);
        bool privLoaded = await WaitUntilLoadedAsync(priv);
        await priv.NavigateAsync(new Uri(Path.Combine(pages, "geo.html")));
        bool isPrivate = priv.IsPrivate && priv.WebViewIsPrivate == true && browser.WebViewIsPrivate == false;
        await Note($"a private window runs its web view in private mode (the main window's does not): {(isPrivate ? "pass" : "FAIL")} (loaded={privLoaded})");
        if (!isPrivate) failures.Add("the private window's web view is not in private mode");
        priv.Close();
        context.Step("browser/private done");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
}
