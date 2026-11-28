using System.Diagnostics;
using System.Globalization;
using System.Text;
using Deckle.Browser;
using Deckle.Browser.Views;
using Deckle.Hosting.SelfCheck;
using Windows.Media.Control;
using static Deckle.Hosting.SelfCheck.SelfCheckCapture;

namespace Deckle.Browser.App;

// The comfort part of the Browser self-check: the history's suggestions, find in
// page, the usual shortcuts and the media card, on local pages only (the virtual
// host browser.test mapped to the walk's pages folder, so nothing leaves the
// machine). Each gesture is a step with its duration, in measures.txt and in the
// history line of the run (WalkMeasures), as the gestures walk does.
//
// How each is pressed: a button through its UI Automation peer (the find bar's
// previous, next and close; the media card's play/pause, mute, close and title);
// a text box by its text (the address field, the find box), which makes the
// suggestions and the search as typing does. Not pressed through the control:
//   - Ctrl+F, and every other shortcut: the window is shown with WS_EX_NOACTIVATE
//     and never takes the keyboard, and a key sent to the system would reach what the
//     maintainer is typing into. The walk runs the command a shortcut runs
//     ("via handler") and says so; no key is pressed.
//   - a line of the address field's list, when the line offers no invoke: the
//     field's own handler is run with the suggestion (the walk says which).
//   - the audio's first start, which a page needs a gesture for: a script run
//     through the DevTools protocol with userGesture, the way a click would count.
internal static class ComfortWalk
{
    private const string Host = "browser.test";
    private const string Origin = "https://browser.test";

    private static readonly (string File, string Title)[] Pages =
    [
        ("page1.html", "Comfort page one"),
        ("page2.html", "Comfort page two"),
        ("page3.html", "Comfort page three"),
    ];

    private const string NeedlePage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Haystack</title></head>"
        + "<body style=\"margin:0;background:#1f3a5f;color:#fff;font-family:Segoe UI,sans-serif\">"
        + "<h1 style=\"padding:32px 48px 0\">A haystack</h1>"
        + "<p style=\"padding:0 48px\">There is a needle in the first line of this page.</p>"
        + "<p style=\"padding:0 48px\">The second line has another needle, and nothing else.</p>"
        + "<p style=\"padding:0 48px\">The last line ends with a needle.</p></body></html>";

    private const string AudioPage =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Audio page</title></head>"
        + "<body style=\"margin:0;background:#4a235a;color:#fff;font-family:Segoe UI,sans-serif\">"
        + "<h1 style=\"padding:48px\">A page that plays a tone</h1>"
        + "<audio id=\"a\" src=\"tone.wav\" loop></audio>"
        + "<script>if (navigator.mediaSession) navigator.mediaSession.metadata = new MediaMetadata({ title: 'Walk tone', artist: 'Deckle' });</script>"
        + "</body></html>";

    // A tone in a WAV file: 16-bit mono, 6 seconds, generated here.
    private static byte[] Tone()
    {
        const int rate = 22050, seconds = 6;
        int samples = rate * seconds;
        using var memory = new MemoryStream();
        using (var w = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + samples * 2);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(samples * 2);
            for (int i = 0; i < samples; i++)
                w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 9000));
        }
        return memory.ToArray();
    }

    private sealed class Step(string name)
    {
        public string Name { get; } = name;
        public string Via { get; set; } = "control";
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public void Mark() => Clock.Restart();
    }

    public static async Task RunAsync(BrowserWalkContext context, BrowserWindow browser, bool captureFind, bool captureMedia, bool loaded)
    {
        string measures = Path.Combine(context.OutputFolder, "measures.txt");
        string pages = Path.Combine(context.OutputFolder, "pages");
        Directory.CreateDirectory(pages);
        foreach ((string file, string title) in Pages)
        {
            await File.WriteAllTextAsync(Path.Combine(pages, file),
                $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{title}</title></head>"
                + "<body style=\"margin:0;background:#154360;color:#fff;font-family:Segoe UI,sans-serif\">"
                + $"<h1 style=\"padding:48px\">{title}</h1></body></html>");
        }
        await File.WriteAllTextAsync(Path.Combine(pages, "needle.html"), NeedlePage);
        await File.WriteAllTextAsync(Path.Combine(pages, "audio.html"), AudioPage);
        await File.WriteAllBytesAsync(Path.Combine(pages, "tone.wav"), Tone());
        browser.MapVirtualHost(Host, pages);

        var failures = new List<string>();
        Task Note(string line) => File.AppendAllTextAsync(measures, line + Environment.NewLine);

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

        browser.SetPaneOpen(true);
        await Task.Delay(400);

        // ── History: three local pages visited, then the suggestions ─────
        Guid home = browser.Session.SelectedId ?? Guid.Empty;
        var visited = new List<Guid>();
        foreach ((string file, _) in Pages)
        {
            visited.Add(await browser.OpenTabAsync(new Uri($"{Origin}/{file}")));
            await Task.Delay(200);
        }
        long before = browser.RequestsSeenForReview;
        IReadOnlyList<string> titles = browser.SuggestForReview("page");
        long requested = browser.RequestsSeenForReview - before;
        bool suggested = titles.Count >= 3 && titles.All(t => t.StartsWith("Comfort page", StringComparison.Ordinal));
        await Note($"history: {browser.HistoryCountForReview} pages kept; the suggestion source called with \"page\" returned {titles.Count} titles "
                   + $"({string.Join(" | ", titles)}): {(suggested ? "pass" : "FAIL")}; "
                   + $"web requests made for it: {requested}: {(requested == 0 ? "pass" : "FAIL")}");
        if (!suggested) failures.Add($"the suggestions for \"page\" were: {string.Join(" | ", titles)}");
        if (requested != 0) failures.Add($"{requested} web requests were made for the suggestions");

        await Gesture("address-type-shows-suggestions", async s =>
        {
            s.Via = "the typed text, which makes the list as typing does";
            s.Mark();
            Check(browser.TypeAddressForReview("page"), $"the field did not take the typed text ({browser.TypeAddressNote})");
            await WaitUntil(() => browser.SuggestionTitlesForReview.Count >= 3, "the list of suggestions");
        });

        await Gesture("address-suggestion-chosen", async s =>
        {
            int index = browser.SuggestionTitlesForReview.ToList().FindIndex(t => t == "Comfort page two");
            Check(index >= 0, "\"Comfort page two\" was not among the suggestions");
            s.Mark();
            string? how = await browser.ChooseSuggestionForReviewAsync(index);
            Check(how is not null, "no such suggestion");
            s.Via = how!;
            await WaitUntil(() => browser.ShownTitleForReview == "Comfort page two", "the chosen page's title");
        });

        // ── Shortcuts, as their commands run ─────────────────────────────
        await Gesture("shortcut-reopen-closed-tab", async s =>
        {
            s.Via = "handler (no key is pressed: the window never takes the keyboard)";
            Guid extra = await browser.OpenTabAsync(new Uri($"{Origin}/{Pages[2].File}"));
            await Task.Delay(300);
            browser.CloseTab(extra);
            int tabs = browser.Session.Tabs.Count;
            s.Mark();
            browser.RunShortcutForReview(ShortcutCommand.ReopenClosedTab);
            await WaitUntil(() => browser.Session.Tabs.Count == tabs + 1, "the closed tab opened again");
            await WaitUntil(() => browser.ShownTitleForReview == "Comfort page three", "its page again");
        });
        await Gesture("shortcut-previous-and-next-tab", async s =>
        {
            s.Via = "handler (no key is pressed: the window never takes the keyboard)";
            Guid at = browser.Session.SelectedId ?? Guid.Empty;
            s.Mark();
            browser.RunShortcutForReview(ShortcutCommand.PreviousTab);
            await WaitUntil(() => browser.Session.SelectedId != at, "another tab selected");
            browser.RunShortcutForReview(ShortcutCommand.NextTab);
            await WaitUntil(() => browser.Session.SelectedId == at, "the first tab selected again");
        });
        await Gesture("shortcut-focus-address", async s =>
        {
            s.Via = "handler (no key is pressed: the window never takes the keyboard)";
            s.Mark();
            browser.RunShortcutForReview(ShortcutCommand.FocusAddress);
            await WaitUntil(() => browser.AddressFocusedForReview, "the address field focused");
        });
        foreach (Guid id in visited.Concat(browser.Session.Tabs.Select(t => t.Id).Where(i => i != home)).ToList())
            browser.CloseTab(id);
        await browser.SelectAsync(home);
        await Task.Delay(300);

        // ── Find in page ─────────────────────────────────────────────────
        browser.SetPaneOpen(false);
        await Task.Delay(500);
        browser.SetPaneOpen(true);
        await Task.Delay(800);
        Guid haystack = await browser.OpenTabAsync(new Uri($"{Origin}/needle.html"));
        await Task.Delay(500);
        await Gesture("find-open", async s =>
        {
            s.Via = "handler (Ctrl+F is a key; the window's own method runs what it runs)";
            s.Mark();
            browser.OpenFindForReview();
            await WaitUntil(() => browser.FindBarShownForReview, "the find bar shown");
        });
        await Gesture("find-type-needle", async s =>
        {
            s.Via = "the typed text, which searches as typing does";
            s.Mark();
            browser.TypeFindForReview("needle");
            await WaitUntil(() => browser.FindMatchesForReview == 3, "three matches");
        });
        await Note($"find: \"needle\" in a page that holds it 3 times: {browser.FindMatchesForReview} matches, the bar says \"{browser.FindCountTextForReview}\": "
                   + (browser.FindMatchesForReview == 3 ? "pass" : "FAIL"));
        await Task.Delay(500);
        if (captureFind)
        {
            context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                context.Run, browser, "browser", "find", "Find in page: three matches of one word", loaded, 0));
            context.Step("browser/find captured");
        }
        await Gesture("find-next", async s =>
        {
            int active = browser.FindActiveForReview;
            s.Mark();
            Check(browser.ClickFindNextForReview(), "the next button offered no invoke");
            await WaitUntil(() => browser.FindActiveForReview != active, "another match active");
        });
        await Gesture("find-previous", async s =>
        {
            int active = browser.FindActiveForReview;
            s.Mark();
            Check(browser.ClickFindPreviousForReview(), "the previous button offered no invoke");
            await WaitUntil(() => browser.FindActiveForReview != active, "another match active");
        });
        await Gesture("find-close", async s =>
        {
            s.Mark();
            Check(browser.ClickFindCloseForReview(), "the close button offered no invoke");
            await WaitUntil(() => !browser.FindBarShownForReview, "the bar hidden");
        });
        browser.CloseTab(haystack);
        await browser.SelectAsync(home);
        await Task.Delay(300);

        // ── The media card ───────────────────────────────────────────────
        browser.SetPaneOpen(false);
        await Task.Delay(500);
        browser.SetPaneOpen(true);
        await Task.Delay(800);
        await Note($"panel open before the media card: {browser.PaneOpenForReview} (narrow state {browser.PaneOverPageForReview})");
        Guid audio = await browser.OpenTabAsync(new Uri($"{Origin}/audio.html"));
        await Task.Delay(300);
        await browser.ExecuteScriptAsync("document.getElementById('a').play(); 0");
        bool shown = await Until(() => browser.MediaCardShownForReview, 4000);
        string startedBy = "ExecuteScriptAsync";
        if (!shown)
        {
            // A page may not start sound on its own: the start is made a gesture's (DevTools protocol, userGesture).
            await browser.CallDevToolsForReviewAsync("Runtime.evaluate",
                "{\"expression\":\"document.getElementById('a').play()\",\"userGesture\":true,\"awaitPromise\":true}");
            startedBy = "a DevTools Runtime.evaluate with userGesture (ExecuteScriptAsync alone started no sound)";
            shown = await Until(() => browser.MediaCardShownForReview, 8000);
        }
        await Note($"media: the audio was started by {startedBy}; the card shows: {(shown ? "pass" : "FAIL")}");
        if (shown)
        {
            await Until(() => browser.MediaReadForReview && browser.MediaTitleForReview == "Walk tone", 5000);
            await Task.Delay(1500);
            await Note($"media: the card shows the title \"{browser.MediaTitleForReview}\" and the artist \"{browser.MediaArtistForReview}\": "
                       + (browser.MediaTitleForReview == "Walk tone" && browser.MediaArtistForReview == "Deckle" ? "pass" : "FAIL"));
            if (browser.MediaTitleForReview != "Walk tone") failures.Add($"the media card's title read \"{browser.MediaTitleForReview}\"");
            if (captureMedia)
            {
                context.Report.Surfaces.Add(await CaptureWindowFrameAsync(
                    context.Run, browser, "browser", "media", "The media card while a tone plays", loaded, 0));
                context.Step("browser/media captured");
            }
            await SessionProbe(Note);
        }
        else
        {
            failures.Add("the media card did not show for a page that plays a tone");
        }

        if (shown)
        {
            await Gesture("media-play-pause-pauses", async s =>
            {
                Check(!browser.MediaPausedForReview, "the media was already paused");
                s.Mark();
                Check(browser.ClickMediaPlayPauseForReview(), "the play/pause button offered no invoke");
                await WaitUntil(() => browser.MediaPausedForReview, "the card reading paused");
                string paused = await browser.ExecuteScriptAsync("document.getElementById('a').paused");
                Check(paused == "true", $"the page's audio element reads paused={paused}");
                await Note("media: paused through the card; the page's audio element says paused=true");
            });
            await Gesture("media-play-pause-resumes", async s =>
            {
                s.Mark();
                Check(browser.ClickMediaPlayPauseForReview(), "the play/pause button offered no invoke");
                await WaitUntil(() => !browser.MediaPausedForReview, "the card reading playing");
            });
            await Gesture("media-mute", async s =>
            {
                s.Mark();
                Check(browser.ClickMediaMuteForReview(), "the mute button offered no invoke");
                await WaitUntil(() => browser.MediaMutedForReview, "the page muted");
                Check(browser.ClickMediaMuteForReview(), "the mute button offered no invoke the second time");
                await WaitUntil(() => !browser.MediaMutedForReview, "the page not muted");
            });
            await Gesture("media-title-selects-the-playing-item", async s =>
            {
                await browser.SelectAsync(home);
                await Task.Delay(300);
                Check(browser.Session.SelectedId == home, "the first item was not selected");
                s.Mark();
                Check(browser.ClickMediaTitleForReview(), "the title offered no invoke");
                await WaitUntil(() => browser.Session.SelectedId == audio, "the playing item selected");
            });
            await Gesture("media-close", async s =>
            {
                s.Mark();
                Check(browser.ClickMediaCloseForReview(), "the close button offered no invoke");
                await WaitUntil(() => !browser.MediaCardShownForReview, "the card hidden");
                await Task.Delay(500);
                string paused = await browser.ExecuteScriptAsync("document.getElementById('a').paused");
                Check(paused == "true", $"the page's audio element reads paused={paused} after close");
            });
        }
        browser.CloseTab(audio);
        await browser.SelectAsync(home);
        await Task.Delay(300);
        context.Step("browser/comfort done");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));

        static async Task<bool> Until(Func<bool> condition, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > timeoutMs) return false;
                await Task.Delay(50);
            }
            return true;
        }
    }

    // The first thing the media card's source was tried against: whether Windows
    // lists a media session for the tone the page plays (the global transport
    // controls the volume flyout and the media keys use). Only whether one carries the
    // tone's title, and how many sessions there are, are noted: what the other
    // sessions play is the maintainer's and is not read.
    private static async Task SessionProbe(Func<string, Task> note)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionManager manager =
                await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = manager.GetSessions();
            bool found = false;
            foreach (GlobalSystemMediaTransportControlsSession session in sessions)
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                if (properties.Title != "Walk tone") continue;
                found = true;
                var timeline = session.GetTimelineProperties();
                var info = session.GetPlaybackInfo();
                var controls = info.Controls;
                await note($"media session of the tone: source app id \"{session.SourceAppUserModelId}\", artist \"{properties.Artist}\", "
                           + $"status {info.PlaybackStatus}, position {timeline.Position.TotalSeconds:0.0} s of {timeline.EndTime.TotalSeconds:0.0} s, "
                           + $"play/pause enabled {controls.IsPlayPauseToggleEnabled}, next enabled {controls.IsNextEnabled}, previous enabled {controls.IsPreviousEnabled}");
            }
            await note($"media sessions of Windows: {sessions.Count} session(s); one carries the tone's title: {(found ? "YES" : "no")}");
        }
        catch (Exception ex)
        {
            await note($"media sessions of Windows: could not be asked ({ex.GetType().Name})");
        }
    }
}
