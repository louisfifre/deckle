using System.Diagnostics;
using System.Globalization;
using Deckle.Browser;
using Deckle.Browser.Views;

namespace Deckle.Browser.App;

// The gestures part of the Browser self-check: the app used the way a person
// uses it, through the controls themselves, on local pages only (the virtual
// host browser.test mapped to the walk's pages folder, so nothing leaves the
// machine). Each gesture is a step that records pass or fail and the time from
// the press to what it changes being there; the figures go in measures.txt and
// in the history line of the run (WalkMeasures).
//
// How each is pressed is the window's (BrowserWindow.Gestures): a button, a menu
// entry, a dialog's button or an item container through its UI Automation
// peer; a text field, whose peer offers no value pattern, by its Text. Seven are not pressed through
// the control itself, and the walk says so beside each of them ("via handler"):
//   Enter and Escape in the address field, a middle click on a tab and the drag
//   of a tab, for want of a keyboard or a pointer to press them with in a window
//   that never takes the focus; the notice of HTTPS first, which the window is
//   made to show as a failed upgrade would (the button is then really pressed);
//   and the crash notice, when the engine's chrome://kill does not end the
//   page's process.
// The durations carry the poll's granularity (about 15 ms on Windows).
internal static class GesturesWalk
{
    private const string Host = "browser.test";
    private const string Origin = "https://browser.test";

    private static readonly (string File, string Title, string Colour)[] Pages =
    [
        ("g1.html", "Gesture one", "#1b4f72"),
        ("g2.html", "Gesture two", "#145a32"),
        ("g3.html", "Gesture three", "#7b241c"),
    ];

    private sealed class Step(string name)
    {
        public string Name { get; } = name;
        public string Via { get; set; } = "control";
        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        // The press is about to happen: what came before was setting up.
        public void Mark() => Clock.Restart();
    }

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser)
    {
        string measures = Path.Combine(context.OutputFolder, "measures.txt");
        string pages = Path.Combine(context.OutputFolder, "pages");
        Directory.CreateDirectory(pages);
        foreach ((string file, string title, string colour) in Pages)
        {
            await File.WriteAllTextAsync(Path.Combine(pages, file),
                $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{title}</title></head>"
                + $"<body style=\"margin:0;background:{colour};color:#fff;font-family:Segoe UI,sans-serif\">"
                + $"<h1 style=\"padding:48px\">{title}</h1></body></html>");
        }
        if (!File.Exists(Path.Combine(pages, "geo.html")))
        {
            await File.WriteAllTextAsync(Path.Combine(pages, "geo.html"),
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>asking</title></head><body>"
                + "<script>navigator.geolocation.getCurrentPosition(function () { document.title = 'allowed'; },"
                + "function (e) { document.title = 'denied:' + e.code; });</script></body></html>");
        }
        browser.MapVirtualHost(Host, pages);

        var failures = new List<string>();
        Uri Page(int n) => new($"{Origin}/{Pages[n].File}");

        async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 8000)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"{what}: not there after {timeoutMs} ms");
                await Task.Delay(10);
            }
        }

        static void Check(bool holds, string what)
        {
            if (!holds) throw new InvalidOperationException(what);
        }

        // One gesture: a step that records its outcome and duration and never
        // stops the walk; a failure is told at the end.
        async Task Gesture(string name, Func<Step, Task> body)
        {
            var step = new Step(name);
            string outcome;
            try
            {
                await body(step);
                step.Clock.Stop();
                outcome = "pass";
                context.Measures.GestureMs[name] = step.Clock.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                step.Clock.Stop();
                outcome = $"FAIL ({ex.GetType().Name}: {ex.Message})";
                context.Measures.GestureFailures.Add(name);
                failures.Add($"gesture {name}: {ex.Message}");
            }
            await File.AppendAllTextAsync(measures, string.Create(CultureInfo.InvariantCulture,
                $"gesture {name} (via {step.Via}): {step.Clock.Elapsed.TotalMilliseconds:0} ms, {outcome}{Environment.NewLine}"));
            context.Step($"browser/gesture {name}");
        }

        // A tab on a page of the walk, shown and loaded, as the walk's own set-up.
        async Task<Guid> OpenAsync(int n)
        {
            Guid id = await browser.OpenTabAsync(Page(n));
            await Task.Delay(300);
            return id;
        }

        async Task<string> TitleOfPage() => await browser.ExecuteScriptAsync("document.title");

        browser.SetPaneOpen(true);
        await Task.Delay(400);
        Guid first = await OpenAsync(0);

        // ── The address field ────────────────────────────────────────────
        await Gesture("address-type-enter", async s =>
        {
            s.Via = "the typed text, then the handler's commit for Enter";
            Check(browser.TypeAddressForReview(Page(1).AbsoluteUri), $"the field did not take the typed address ({browser.TypeAddressNote})");
            s.Mark();
            browser.PressEnterInAddressForReview();
            await WaitUntil(() => browser.ShownTitleForReview == "Gesture two", "the typed page's title");
        });

        string shownAddress = browser.AddressTextForReview;
        await Gesture("address-escape", async s =>
        {
            s.Via = "the typed text, then the handler's cancel for Escape";
            Check(browser.TypeAddressForReview("something typed and then given up"), $"the field did not take the text ({browser.TypeAddressNote})");
            s.Mark();
            browser.PressEscapeInAddressForReview();
            await WaitUntil(() => browser.AddressTextForReview == shownAddress, "the address back in the field");
        });

        // ── Back, forward, reload ────────────────────────────────────────
        await Gesture("back", async s =>
        {
            await WaitUntil(() => browser.BackEnabledForReview, "the back button enabled");
            s.Mark();
            Check(browser.ClickBackForReview(), "the back button offered no invoke");
            await WaitUntil(() => browser.ShownTitleForReview == "Gesture one", "the first page again");
        });
        await Gesture("forward", async s =>
        {
            await WaitUntil(() => browser.ForwardEnabledForReview, "the forward button enabled");
            s.Mark();
            Check(browser.ClickForwardForReview(), "the forward button offered no invoke");
            await WaitUntil(() => browser.ShownTitleForReview == "Gesture two", "the second page again");
        });
        await Gesture("reload", async s =>
        {
            int before = browser.CompletedNavigationsForReview;
            s.Mark();
            Check(browser.ClickReloadForReview(), "the reload button offered no invoke");
            await WaitUntil(() => browser.CompletedNavigationsForReview > before, "a navigation completed after the reload");
        });

        // ── The panel: closed to the rail and open again, then narrow ────
        await Gesture("panel-to-rail-and-back", async s =>
        {
            Check(browser.PaneOpenForReview, "the panel did not start open");
            s.Mark();
            Check(browser.ClickPaneToggleForReview(), "the panel's button offered no invoke");
            await WaitUntil(() => !browser.PaneOpenForReview, "the panel in its rail");
            Check(browser.ClickPaneToggleForReview(), "the rail's button offered no invoke");
            await WaitUntil(() => browser.PaneOpenForReview, "the panel open again");
        });
        await Gesture("panel-narrow-window", async s =>
        {
            s.Mark();
            browser.ResizeForReview(560, 800);
            await WaitUntil(() => browser.PaneOverPageForReview, "the window's narrow state");
            if (browser.PaneOpenForReview)
            {
                Check(browser.ClickPaneToggleForReview(), "the panel's button offered no invoke");
                await WaitUntil(() => !browser.PaneOpenForReview, "the narrow panel hidden");
            }
            await WaitUntil(() => browser.NarrowToggleShownForReview, "the button over the page");
            Check(browser.ClickNarrowToggleForReview(), "the button over the page offered no invoke");
            await WaitUntil(() => browser.PaneOpenForReview, "the panel over the page");
            browser.ResizeForReview(1280, 860);
            await WaitUntil(() => !browser.PaneOverPageForReview, "the window wide again");
            browser.SetPaneOpen(true);
            await Task.Delay(300);
        });

        // ── Tabs ─────────────────────────────────────────────────────────
        int tabsBefore = browser.Session.Tabs.Count;
        await Gesture("new-tab-line", async s =>
        {
            s.Mark();
            Check(browser.ClickNewTabForReview(), "the New Tab line offered no invoke");
            await WaitUntil(() => browser.Session.Tabs.Count == tabsBefore + 1, "a tab added");
            await WaitUntil(() => browser.AddressHasFocusForReview, "the address field focused");
        });
        await Gesture("tab-close-button", async s =>
        {
            Guid added = browser.Session.SelectedId ?? throw new InvalidOperationException("no tab selected");
            s.Mark();
            Check(browser.ClickCloseButtonOfTabForReview(added), "the tab's close button was not there or offered no invoke");
            await WaitUntil(() => browser.Session.Tabs.Count == tabsBefore, "the tab closed");
        });
        await Gesture("tab-middle-click", async s =>
        {
            s.Via = "handler (a pointer event cannot be built outside the framework)";
            Guid tab = await OpenAsync(2);
            int count = browser.Session.Tabs.Count;
            s.Mark();
            Check(browser.MiddleClickTabForReview(tab), "the tab was not found");
            await WaitUntil(() => browser.Session.Tabs.Count == count - 1, "the tab closed");
        });
        await Gesture("tab-drag-to-another-place", async s =>
        {
            s.Via = "handler after the list's items were moved (no pointer to drag with)";
            Guid second = await OpenAsync(1);
            Guid third = await OpenAsync(2);
            Check(browser.Session.Tabs.Count >= 3, "fewer than three tabs to reorder");
            s.Mark();
            Check(browser.DragTabForReview(third, 0), "the tab could not be dragged");
            await WaitUntil(() => browser.Session.Tabs[0].Id == third, "the session's order changed");
            Check(browser.TabOrderForReview.SequenceEqual(browser.Session.Tabs.Select(t => t.Id)), "the list and the session disagree on the order");
            browser.CloseTab(third);
            browser.CloseTab(second);
        });

        // ── Pin and unpin through the context menus ──────────────────────
        Guid pinned = await OpenAsync(1);
        await Gesture("tab-menu-pin", async s =>
        {
            IReadOnlyList<BrowserWindow.ReviewMenuEntry> entries = await browser.OpenTabMenuForReviewAsync(pinned);
            Check(entries.Any(e => e.AutomationId == "TabMenu_Pin" && e.Drawn), "the tab's menu did not draw its Pin entry");
            s.Mark();
            Check(browser.ChooseMenuEntryForReview("TabMenu_Pin"), "the Pin entry offered no invoke");
            await WaitUntil(() => browser.PinnedOrderForReview.Contains(pinned) && browser.PinnedTileDrawnForReview(pinned), "the tile in the grid");
        });
        await Gesture("tile-menu-unpin", async s =>
        {
            IReadOnlyList<BrowserWindow.ReviewMenuEntry> entries = await browser.OpenTileMenuForReviewAsync(pinned);
            Check(entries.Any(e => e.AutomationId == "TileMenu_Unpin" && e.Drawn), "the tile's menu did not draw its Unpin entry");
            s.Mark();
            Check(browser.ChooseMenuEntryForReview("TileMenu_Unpin"), "the Unpin entry offered no invoke");
            await WaitUntil(() => browser.TabOrderForReview.Contains(pinned) && !browser.PinnedOrderForReview.Contains(pinned), "the tab back in the list");
        });
        browser.CloseTab(pinned);

        // ── A pinned tile clicked: loads the first time, wakes after ─────
        Guid site = browser.AddPinnedEntryForReview(Page(1));
        await Task.Delay(400);
        await Gesture("pinned-tile-first-click-loads", async s =>
        {
            Check(!browser.HasWebViewForReview(site), "the pinned entry already had a web view");
            Check(browser.PinnedTileDrawnForReview(site), "the pinned entry's tile is not drawn");
            s.Mark();
            Check(browser.ClickPinnedTileForReview(site), "the tile offered no invoke");
            await browser.WaitAnswersForReviewAsync(site);
            Check(browser.Session.SelectedId == site, "the site was not selected");
        });
        await Gesture("pinned-tile-later-click-wakes", async s =>
        {
            await browser.SelectAsync(first);
            await Task.Delay(500);
            await browser.SuspendHiddenAsync();
            await Task.Delay(1000);
            Check(!browser.IsAwakeForReview(site), "the pinned site was not suspended");
            s.Mark();
            Check(browser.ClickPinnedTileForReview(site), "the tile offered no invoke");
            await browser.WaitAnswersForReviewAsync(site);
            Check(browser.IsAwakeForReview(site), "the pinned site did not wake");
        });
        await browser.OpenTileMenuForReviewAsync(site);
        browser.ChooseMenuEntryForReview("TileMenu_Unpin");
        await Task.Delay(300);
        browser.CloseTab(site);
        await browser.SelectAsync(first);
        await Task.Delay(300);

        // ── The foot's and the address row's flyouts ─────────────────────
        await Gesture("downloads-flyout", async s =>
        {
            s.Mark();
            Check(browser.ClickDownloadsForReview(), "the downloads button offered no invoke");
            await WaitUntil(() => browser.DownloadsFlyoutOpenForReview, "the flyout open");
        });
        browser.HideDownloads();
        await Task.Delay(300);
        await Gesture("site-settings-flyout", async s =>
        {
            s.Mark();
            Check(browser.ClickSiteSettingsForReview(), "the site-settings icon was disabled or offered no invoke");
            await WaitUntil(() => browser.SiteSettingsFlyoutOpenForReview, "the flyout open");
        });
        browser.HideSiteSettingsForReview();
        await Task.Delay(300);

        // ── The notices: "Continue anyway" and "Reload" ──────────────────
        await Gesture("notice-continue-anyway", async s =>
        {
            s.Via = "control (the notice was shown by the window's method, as a failed upgrade shows it)";
            int before = browser.CompletedNavigationsForReview;
            browser.ShowNoSecureNoticeForReview(new Uri($"http://{Host}/{Pages[1].File}"));
            await WaitUntil(() => browser.NoticeOpenForReview, "the notice open");
            Check(browser.NoticeButtonTextForReview.Contains("Continue", StringComparison.OrdinalIgnoreCase),
                $"the notice's button reads \"{browser.NoticeButtonTextForReview}\"");
            s.Mark();
            Check(browser.ClickNoticeButtonForReview(), "the notice's button offered no invoke");
            await WaitUntil(() => !browser.NoticeOpenForReview && browser.HttpAllowedForReview(Host), "the notice closed and http allowed");
            await WaitUntil(() => browser.CompletedNavigationsForReview > before, "the page loaded");
        });
        await Gesture("notice-reload-after-crash", async s =>
        {
            browser.KillRenderProcessForReview();
            bool crashed = false;
            try
            {
                await WaitUntil(() => browser.NoticeForReview == PageNotice.Crashed, "the engine's crash notice", 6000);
                crashed = true;
            }
            catch (TimeoutException)
            {
                // chrome://kill did not end the process on this page: the notice is shown by the window's method.
            }
            if (!crashed)
            {
                s.Via = "control (chrome://kill ended no process here; the notice was shown by the window's method)";
                browser.ShowCrashedNoticeForReview();
            }
            else
            {
                s.Via = "control (the notice came from a render process ended with chrome://kill)";
            }
            await WaitUntil(() => browser.NoticeOpenForReview, "the notice open");
            Check(browser.NoticeButtonTextForReview.Contains("Reload", StringComparison.OrdinalIgnoreCase),
                $"the notice's button reads \"{browser.NoticeButtonTextForReview}\"");
            int before = browser.CompletedNavigationsForReview;
            s.Mark();
            Check(browser.ClickNoticeButtonForReview(), "the notice's button offered no invoke");
            await WaitUntil(() => !browser.NoticeOpenForReview, "the notice closed");
            await WaitUntil(() => browser.CompletedNavigationsForReview > before, "the page loaded again");
        });

        // ── The permission dialog's Block button ─────────────────────────
        browser.ForgetPermissionForReview(Origin, PermissionKind.Geolocation);
        await Gesture("permission-dialog-block-button", async s =>
        {
            Task<Guid> opening = browser.OpenTabAsync(new Uri($"{Origin}/geo.html"));
            Check(await browser.WaitForPermissionQuestionAsync(TimeSpan.FromSeconds(20)), "no permission question appeared");
            await Task.Delay(500);
            s.Mark();
            Check(browser.ClickPermissionButtonForReview(allow: false), "the dialog's Block button was not found or offered no invoke");
            await WaitUntil(() => browser.StoredPermissionOf(Origin, PermissionKind.Geolocation) == PermissionDecision.Block, "the answer stored");
            Guid tab = await opening;
            await WaitUntil(() => browser.ShownTitleForReview == "denied:1", "the page seeing the refusal");
            browser.CloseTab(tab);
        });

        await browser.SelectAsync(first);
        await Task.Delay(300);
        context.Step("browser/gestures done");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
}
