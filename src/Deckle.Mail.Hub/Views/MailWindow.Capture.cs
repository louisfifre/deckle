using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Mail.Hub;

// One step of the walk: the page's slug, its label for a reader, whether the
// whole window is worth a picture there too (the rail and the title bar
// change), and whether its page loaded before the timeout. KeepsPopup marks a
// step whose subject is a menu, a flyout or a dialog left open on purpose:
// the window is photographed with it, and the walk closes it afterwards.
public readonly record struct MailCaptureVisit(string Slug, string Label, bool WholeWindow, bool Loaded, long LoadMs, bool KeepsPopup = false);

// ── The page walk of the self-check ───────────────────────────────────────────
//
// The self-check shows each surface the maintainer drew, in a known state, and
// photographs it: Triage on a normal day and after gestures, an opened subject,
// Inbox and alias pages, Activity's two parts, Settings, a mailbox page, Add
// mailbox, then each scenario of the sample. Gestures are made through the
// session, as a click would make them; no dialog opens (the first-trash notice
// belongs to a click, not to the session). The window ends on a fresh normal
// day.
//
// The steps that keep a popup open show it constrained to the window
// (ShouldConstrainToRootBounds), so it is drawn inside the window's own
// surface and the window capture sees it; a menu opened by a person is not
// constrained and may leave the window's edge. A dialog is opened through the
// same call its button makes and is hidden afterwards, so its await ends as a
// Cancel would. The narrow and the light steps change the window and put it
// back after their picture.
public sealed partial class MailWindow
{
    private static readonly TimeSpan PageSettleDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PageLoadTimeout = TimeSpan.FromSeconds(3);

    private sealed record CaptureStep(string Slug, string Label, bool WholeWindow, Action<MailWindow> Prepare)
    {
        public bool KeepsPopup { get; init; }
        // Run once the page has settled: a popup opened from Prepare would
        // open before a freshly loaded scenario has built its rows.
        public Action<MailWindow>? Then { get; init; }
        public Action<MailWindow>? After { get; init; }
    }

    // The narrow frames are drawn 640 by 860.
    private const int NarrowWidth = 640;
    private const int NarrowHeight = 860;
    // A little over the window's floor (400), where a person's screenshot
    // showed the title a few letters a line.
    private const int NarrowestWidth = 430;
    // Just under 1008, where the navigation folds to its rail of icons.
    private const int RailWidth = 1000;
    private Windows.Graphics.SizeInt32? _sizeBeforeNarrow;
    private (ElementTheme Content, TitleBarTheme TitleBar)? _themeBeforeLight;

    private static readonly CaptureStep[] Steps =
    [
        // The navigation folded by its button in a wide window, then opened
        // again: the page takes the room and gives it back.
        //
        // These two come first, on the navigation as the window built it.
        // Once the rail has been rebuilt in a loaded window (UseScenario
        // below), setting IsPaneOpen from here throws E_FAIL out of WinUI and
        // a folded rail keeps the room of its groups' children: seen on 11
        // November 2026 with and without ComponentMotion, cause not found.
        // The narrow steps further down show that rail.
        new("pane-folded", "2.1 Triage, the navigation folded by its button", true, w =>
        {
            w.Go(MailDestination.Triage);
            w.Nav.IsPaneOpen = false;
        })
        {
            After = w => w.Nav.IsPaneOpen = true,
        },
        new("pane-reopened", "2.1 Triage, the navigation opened again", true, _ => { }),
        new("triage", "2.1 Triage, a normal day", true, w => w.UseScenario(SampleScenario.NormalDay)),
        new("triage-gestures", "2.2 Triage after two gestures", false, w =>
        {
            w.Session.AddTask("s-drive", w.Session.Today);
            w.Session.MoveToTrash("s-camille");
        }),
        new("triage-all-treated", "2.7 Everything treated, the day not ended", false, w =>
        {
            w.Session.AddTask("s-france-travail", new DateOnly(2026, 11, 13));
            w.Session.MoveToTrash("s-devis");
        }),
        new("subject", "3.1 A subject opened", false, w =>
        {
            w.UseScenario(SampleScenario.NormalDay);
            w.Go(MailDestination.Subject("s-devis"));
        }),
        new("subject-earlier", "3.2 An earlier mail of the subject", false, w => (w.CurrentPage as SubjectPage)?.ShowMailAt(1)),
        new("subject-treated", "3.3 Just after a gesture on the page", false, w => w.Session.AddTask("s-devis", null)),
        new("inbox", "4.1 Inbox of a mailbox", true, w =>
        {
            w.UseScenario(SampleScenario.NormalDay);
            w.Go(MailDestination.Mailbox("mb-perso"));
        }),
        new("alias", "4.2 An alias", false, w => w.Go(MailDestination.Alias("mb-perso", "al-shop"))),
        new("mailbox-single", "4.5 A mailbox without aliases", false, w => w.Go(MailDestination.Mailbox("mb-maison"))),
        // Activity is first reached the way the set-aside line of Triage
        // reaches it, on the sorting's part; then the person's part is chosen
        // on its tab. In that order the walk sees a tab chosen on a page that
        // opened on the other one.
        new("activity-by-deckle", "5.2 Activity, by Deckle, reached from the set-aside line", true,
            w => w.Go(MailDestination.Activity(ActivityPart.ByDeckle))),
        new("activity", "5.1 Activity, by you, chosen on its tab", false,
            w => (w.CurrentPage as ActivityPage)?.ChoosePart(ActivityPart.ByYou)),
        new("settings", "7.1 Settings", true, w => w.Go(MailDestination.Settings)),
        new("mailbox", "7.2 The page of a mailbox", false, w => w.Go(MailDestination.MailboxSettings("mb-perso"))),
        new("add-mailbox", "1.2 Add mailbox", false, w => w.Go(MailDestination.AddMailbox)),
        new("nothing-waiting", "2.8 Nothing is waiting", true, w => w.UseScenario(SampleScenario.NothingWaiting)),
        new("no-mailbox", "1.1 No mailbox yet", true, w => w.UseScenario(SampleScenario.NoMailbox)),
        new("no-anytype", "7.9 Triage without Anytype", true, w => w.UseScenario(SampleScenario.AnytypeNotConnected)),
        new("no-anytype-settings", "7.7 Anytype not connected, in Settings", false, w => w.Go(MailDestination.Settings)),
        new("cannot-connect", "6.1 A mailbox cannot connect", true, w =>
        {
            w.UseScenario(SampleScenario.MailboxCannotConnect);
            w.Session.AddTask("s-devis", w.Session.Today);
        }),
        new("cannot-connect-mailbox", "7.3 The page of a mailbox that cannot connect", false,
            w => w.Go(MailDestination.MailboxSettings("mb-maison"))),
        new("problems-open", "6.2 The problems list open", true, w => w.OpenProblems(constrainToWindow: true)) { KeepsPopup = true },
        new("marks", "2.5 Rows with a mark", true, w => w.UseScenario(SampleScenario.Marks)),
        new("subject-unverified", "2.5 An unverified sender, opened", false, w => w.Go(MailDestination.Subject("s-colis"))),
        new("subject-unreadable", "2.5 A mail that could not be read, opened", false, w => w.Go(MailDestination.Subject("s-releve"))),
        new("day-menu", "2.3 The menu of days open", true, w => w.UseScenario(SampleScenario.NormalDay))
        {
            KeepsPopup = true,
            Then = w => w.OpenDayMenuForCapture("s-france-travail"),
        },
        new("pick-a-day", "2.3 The calendar of Pick a day", true, w => w.UseScenario(SampleScenario.NormalDay))
        {
            KeepsPopup = true,
            Then = w => (w.CurrentPage as TriagePage)?.RowView("s-france-travail")?.TaskColumn.OpenCalendar(constrainToWindow: true),
        },
        new("first-trash", "1.9 The notice of the first trash", true, w => w.OpenTrashNoticeForCapture("s-camille")) { KeepsPopup = true },
        new("search-menu", "5.4 The search menu with results", true, w => w.OpenSearchForCapture("martin")) { KeepsPopup = true },
        new("remove-confirm", "7.6 Remove a mailbox, confirmation", true, w =>
        {
            w.Go(MailDestination.MailboxSettings("mb-perso"));
            (w.CurrentPage as MailboxSettingsPage)?.AskRemove();
        }) { KeepsPopup = true },
        new("narrow", "2.1 Triage in a narrow window", true, w =>
        {
            w.Go(MailDestination.Triage);
            w._sizeBeforeNarrow = w.AppWindow.Size;
            double scale = w.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            w.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(NarrowWidth * scale), (int)(NarrowHeight * scale)));
        })
        {
            After = w =>
            {
                if (w._sizeBeforeNarrow is { } size) w.AppWindow.Resize(size);
                w._sizeBeforeNarrow = null;
            },
        },
        // Between wide and narrow: the navigation has folded to its rail and
        // the page has gained its width. The list of mails stays under the
        // text, as in the wider window that stacked it first.
        new("subject-rail", "3.1 A subject opened, the navigation folded to its rail", true, w =>
        {
            w.Go(MailDestination.Subject("s-drive"));
            w._sizeBeforeNarrow = w.AppWindow.Size;
            double scale = w.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            w.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(RailWidth * scale), (int)(NarrowHeight * scale)));
        })
        {
            After = w =>
            {
                if (w._sizeBeforeNarrow is { } size) w.AppWindow.Resize(size);
                w._sizeBeforeNarrow = null;
            },
        },
        // The narrowest the window goes: the title keeps the whole width, the
        // two commands stand under the grey line, the search is a button.
        new("subject-narrow", "3.1 A subject opened in the narrowest window", true, w =>
        {
            w.Go(MailDestination.Subject("s-drive"));
            w._sizeBeforeNarrow = w.AppWindow.Size;
            double scale = w.Content?.XamlRoot?.RasterizationScale ?? 1.0;
            w.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(NarrowestWidth * scale), (int)(NarrowHeight * scale)));
        })
        {
            After = w =>
            {
                if (w._sizeBeforeNarrow is { } size) w.AppWindow.Resize(size);
                w._sizeBeforeNarrow = null;
                w.Go(MailDestination.Triage);
            },
        },
        new("light", "2.1 Triage, light theme", true, w =>
        {
            if (w.Content is not FrameworkElement root) return;
            w._themeBeforeLight = (root.RequestedTheme, w.AppWindow.TitleBar.PreferredTheme);
            root.RequestedTheme = ElementTheme.Light;
            w.AppWindow.TitleBar.PreferredTheme = TitleBarTheme.Light;
        })
        {
            After = w =>
            {
                if (w._themeBeforeLight is { } before && w.Content is FrameworkElement root)
                {
                    root.RequestedTheme = before.Content;
                    w.AppWindow.TitleBar.PreferredTheme = before.TitleBar;
                }
                w._themeBeforeLight = null;
            },
        },
    ];

    // The slugs of the walk, in order: what `only` can name.
    public static IReadOnlyList<string> CaptureSlugs { get; } = [.. Steps.Select(s => s.Slug)];

    // Calls visit once per step, the page shown and settled. starting names a
    // step before it begins, so a crash while opening it still says where.
    //
    // only names one step to visit. A step builds on the ones before it (a
    // gesture on the day the step before loaded, a tab chosen on the page it
    // opened), so the walk is played up to that step without a visit, the
    // step is visited, and the walk stops: the caller sees the state the full
    // walk shows there, and nothing after it is opened.
    public async Task WalkCapturesAsync(
        Func<MailCaptureVisit, Task> visit, Action<string, string>? starting = null, string? only = null)
    {
        _suppressTransitions = true;
        try
        {
            foreach (CaptureStep step in Steps)
            {
                bool visited = only is null || step.Slug == only;
                starting?.Invoke(step.Slug, step.Label);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                step.Prepare(this);
                bool loaded = await WaitForPageAsync();
                if (step.Then is not null)
                {
                    step.Then(this);
                    await WaitForPageAsync();
                }
                if (visited)
                    await visit(new MailCaptureVisit(step.Slug, step.Label, step.WholeWindow, loaded, clock.ElapsedMilliseconds, step.KeepsPopup));
                if (step.KeepsPopup) await CloseCapturePopupsAsync();
                step.After?.Invoke(this);
                if (step.After is not null) await WaitForPageAsync();
                if (only is not null && visited) break;
            }
        }
        finally
        {
            UseScenario(SampleScenario.NormalDay);
            _suppressTransitions = false;
        }
    }

    private void OpenDayMenuForCapture(string subjectId)
        => (CurrentPage as TriagePage)?.RowView(subjectId)?.TaskColumn.OpenMenu(constrainToWindow: true);

    // The notice the first trash of a click asks, on that row's subject.
    private void OpenTrashNoticeForCapture(string subjectId)
    {
        if (Content?.XamlRoot is not { } root || Session.FindRow(subjectId) is not { } row) return;
        _ = TrashNotice.AskAsync(root, Session, row.Subject);
    }

    // The menu as typing opens it: the words in the field, the lines under it.
    private void OpenSearchForCapture(string text)
    {
        SearchBox.Text = text;
        SearchBox.ItemsSource = SearchLines(text);
        // The list lives in a popup of the box's template; constrained, it is
        // drawn inside the window where its capture sees it. WinUI refuses the
        // change once a popup has opened, so it is made only before the first
        // opening and never undone (this is the self-check's own instance).
        if (FindPopup(SearchBox) is { ShouldConstrainToRootBounds: false } popup)
            TryConstrain(() => popup.ShouldConstrainToRootBounds = true);
        SearchBox.IsSuggestionListOpen = true;
    }

    // Refused (COMException) on a popup already opened once: it then opens
    // as it is, and the capture shows whatever the window shows.
    internal static void TryConstrain(Action constrain)
    {
        try { constrain(); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private static Microsoft.UI.Xaml.Controls.Primitives.Popup? FindPopup(DependencyObject node)
    {
        if (node is Microsoft.UI.Xaml.Controls.Primitives.Popup popup) return popup;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (FindPopup(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    // A dialog is hidden, so its await ends as Cancel; a menu or a flyout is
    // closed; the field is emptied.
    private async Task CloseCapturePopupsAsync()
    {
        SearchBox.IsSuggestionListOpen = false;
        SearchBox.Text = "";

        if (Content?.XamlRoot is { } root)
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            {
                if (FindDialog(popup.Child) is { } dialog) dialog.Hide();
                else popup.IsOpen = false;
            }
        }
        await NextFrameAsync();
        await Task.Delay(PageSettleDelay);
    }

    // A dialog's popup holds the dialog itself or a layer around it.
    private static ContentDialog? FindDialog(DependencyObject? node)
    {
        if (node is null) return null;
        if (node is ContentDialog dialog) return dialog;
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (FindDialog(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    private async Task<bool> WaitForPageAsync()
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = false;
        while (waited.Elapsed < PageLoadTimeout)
        {
            if (PageFrame.Content is FrameworkElement { IsLoaded: true })
            {
                loaded = true;
                break;
            }
            await Task.Delay(30);
        }
        await NextFrameAsync();
        await NextFrameAsync();
        await Task.Delay(PageSettleDelay);
        return loaded;
    }

    // A window nobody can see may be given no frame: the wait ends after a
    // second so the walk goes on instead of hanging (as SettingsWindow's).
    private static async Task NextFrameAsync()
    {
        var rendered = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        await Task.WhenAny(rendered.Task, Task.Delay(1000));
        CompositionTarget.Rendering -= OnRendering;
    }
}
