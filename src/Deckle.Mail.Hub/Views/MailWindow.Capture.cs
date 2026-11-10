using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Mail.Hub;

// One step of the walk: the page's slug, its label for a reader, whether the
// whole window is worth a picture there too (the rail and the title bar
// change), and whether its page loaded before the timeout.
public readonly record struct MailCaptureVisit(string Slug, string Label, bool WholeWindow, bool Loaded, long LoadMs);

// ── The page walk of the self-check ───────────────────────────────────────────
//
// The self-check shows each surface the maintainer drew, in a known state, and
// photographs it: Triage on a normal day and after gestures, an opened subject,
// Inbox and alias pages, Activity's two parts, Settings, a mailbox page, Add
// mailbox, then each scenario of the sample. Gestures are made through the
// session, as a click would make them; no dialog opens (the first-trash notice
// belongs to a click, not to the session). The window ends on a fresh normal
// day.
public sealed partial class MailWindow
{
    private static readonly TimeSpan PageSettleDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan PageLoadTimeout = TimeSpan.FromSeconds(3);

    private sealed record CaptureStep(string Slug, string Label, bool WholeWindow, Action<MailWindow> Prepare);

    private static readonly CaptureStep[] Steps =
    [
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
        new("activity", "5.1 Activity, by you", true, w => w.Go(MailDestination.Activity(ActivityPart.ByYou))),
        new("activity-by-deckle", "5.2 Activity, by Deckle", false, w => (w.CurrentPage as ActivityPage)?.ShowPart(ActivityPart.ByDeckle)),
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
    ];

    // Calls visit once per step, the page shown and settled. starting names a
    // step before it begins, so a crash while opening it still says where.
    public async Task WalkCapturesAsync(Func<MailCaptureVisit, Task> visit, Action<string, string>? starting = null)
    {
        _suppressTransitions = true;
        try
        {
            foreach (CaptureStep step in Steps)
            {
                starting?.Invoke(step.Slug, step.Label);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                step.Prepare(this);
                bool loaded = await WaitForPageAsync();
                await visit(new MailCaptureVisit(step.Slug, step.Label, step.WholeWindow, loaded, clock.ElapsedMilliseconds));
            }
        }
        finally
        {
            UseScenario(SampleScenario.NormalDay);
            _suppressTransitions = false;
        }
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
