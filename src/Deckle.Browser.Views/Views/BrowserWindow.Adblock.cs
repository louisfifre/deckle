using System.Globalization;
using System.Text;
using System.Text.Json;
using Deckle.Browser.Adblock;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Deckle.Browser.Views;

// ─── Browser window: the ad blocker ──────────────────────────────────────────
//
// The engine is the process's (BrowserAdblock); this file hooks it to each web
// view of the window:
//   - every request a page makes, its frames' included, is put to the engine
//     (WebResourceRequested); one it blocks gets a 403 and is counted for the
//     item; the page itself (a Document request) is never blocked;
//   - at each top-level navigation the engine's cosmetic answer for the target
//     (selectors to hide, scriptlets) becomes the script run when the document
//     is created, replacing the previous navigation's.
//
// NavigationStarting offers no deferral in this SDK, so the script is added
// while the navigation starts but is not awaited: a race exists, in which a
// page whose document is created before the browser process has the script
// runs without its cosmetic filters for that load (requests are not affected).
//
// There is no "disable on this site" yet, and the shield is a state, not a
// toggle. Nothing here logs an address: the count shown is a number.
public sealed partial class BrowserWindow
{
    private void AdblockWire(PanelItem panel, CoreWebView2 core)
    {
        // The overload with source kinds also covers the requests of frames and
        // of workers, which the two-argument one leaves to the page's document.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, args) => OnWebResourceRequested(panel, args);
    }

    private void OnWebResourceRequested(PanelItem panel, CoreWebView2WebResourceRequestedEventArgs args)
    {
        BrowserActivity.Request();
        AdblockEngine? engine = BrowserAdblock.Engine;
        if (engine is null || _environment is null) return;

        string context = args.ResourceContext.ToString();
        if (RequestKind.IsNeverBlocked(context)) return;

        string url = args.Request.Uri;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        string source = panel.PageUrl.Length > 0 ? panel.PageUrl : url;
        if (!engine.ShouldBlock(url, source, RequestKind.ToAdblockType(context))) return;

        args.Response = _environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        panel.BlockedCount++;
        BrowserActivity.Blocked();
        if (_shown == panel) UpdateShield();
    }

    // A top-level navigation starts: the count restarts, and the cosmetic script
    // of the target replaces the previous one.
    private void AdblockNavigationStarting(PanelItem panel, CoreWebView2 core, Uri target, bool redirected)
    {
        if (!redirected)
        {
            panel.BlockedCount = 0;
            panel.PageUrl = target.Scheme is "http" or "https" ? target.AbsoluteUri : "";
            if (_shown == panel) UpdateShield();
        }
        _ = InstallCosmeticAsync(panel, core, target);
    }

    private static async Task InstallCosmeticAsync(PanelItem panel, CoreWebView2 core, Uri target)
    {
        int ticket = ++panel.CosmeticTicket;
        if (panel.CosmeticScriptId is { } previous)
        {
            panel.CosmeticScriptId = null;
            core.RemoveScriptToExecuteOnDocumentCreated(previous);
        }

        AdblockEngine? engine = BrowserAdblock.Engine;
        if (engine is null || target.Scheme is not ("http" or "https")) return;

        string? script = CosmeticScript(engine.Cosmetic(target.AbsoluteUri));
        if (script is null) return;

        string id = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        // A later navigation already asked for its own script: this one is stale.
        if (ticket != panel.CosmeticTicket) core.RemoveScriptToExecuteOnDocumentCreated(id);
        else panel.CosmeticScriptId = id;
    }

    /// <summary>The script that hides the selectors (a constructed stylesheet, which needs no
    /// element of the page yet) and then runs the scriptlets; null when there is nothing to do.</summary>
    internal static string? CosmeticScript(CosmeticResources cosmetic)
    {
        if (cosmetic.HideSelectors.Count == 0 && cosmetic.InjectedScript.Length == 0) return null;

        var script = new StringBuilder();
        if (cosmetic.HideSelectors.Count > 0)
        {
            string css = string.Join("\n", cosmetic.HideSelectors.Select(s => s + " { display: none !important; }"));
            script.Append("(function(){try{var s=new CSSStyleSheet();s.replaceSync(")
                .Append(JsonSerializer.Serialize(css))
                .Append(");document.adoptedStyleSheets=[...document.adoptedStyleSheets,s];}catch(e){}})();\n");
        }
        if (cosmetic.InjectedScript.Length > 0) script.Append(cosmetic.InjectedScript);
        return script.ToString();
    }

    // ── The shield in the address field ──────────────────────────────────────

    private void UpdateShield()
    {
        int blocked = _shown?.BlockedCount ?? 0;
        bool active = BrowserAdblock.Engine is not null;
        string text = active
            ? string.Format(CultureInfo.CurrentCulture, BrowserText.Get("Adblock_Blocked"), blocked)
            : BrowserText.Get("Adblock_Loading");
        ToolTipService.SetToolTip(AdblockShield, text);
        AutomationProperties.SetName(AdblockShield, text);
        AdblockShield.Opacity = active && blocked > 0 ? 1.0 : 0.55;
    }

    // ── For the self-check ───────────────────────────────────────────────────

    /// <summary>Puts an engine of local rules in force in place of the lists'.</summary>
    public static void UseAdblockEngine(AdblockEngine engine) => BrowserAdblock.UseEngine(engine);

    /// <summary>Sets a cookie in the shown page's profile (the by-hand probe's consent cookie).</summary>
    public void SetProbeCookie(string name, string value, string domain)
    {
        CoreWebView2? core = _shown?.View?.CoreWebView2;
        if (core is null) return;
        CoreWebView2Cookie cookie = core.CookieManager.CreateCookie(name, value, domain, "/");
        cookie.IsSecure = true;
        core.CookieManager.AddOrUpdateCookie(cookie);
    }

    /// <summary>The requests blocked for the shown page so far.</summary>
    public int BlockedCount => _shown?.BlockedCount ?? 0;

    /// <summary>The shield's tooltip text, as a person reads it.</summary>
    public string AdblockTooltipText => ToolTipService.GetToolTip(AdblockShield) as string ?? "";
}
