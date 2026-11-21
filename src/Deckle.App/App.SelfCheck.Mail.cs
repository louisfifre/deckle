using Deckle.App.SelfCheck;
using Deckle.Hosting.SelfCheck;
using Deckle.Mail.Hub;
using Deckle.Vision;

namespace Deckle.App;

// The self-check's walk of the Mail window on its built-in sample: the window
// once, then each state the maintainer drew, page by page
// (MailWindow.Capture.cs), with the whole window again where the rail or the
// title bar changes. Run by the harness through MailContribution.
public partial class App
{
    internal async Task WalkMailAsync(SelfCheckWalkContext context)
    {
        SelfCheckReport report = context.Report;
        InterfaceCaptureRun run = context.Run;
        string outputFolder = context.OutputFolder;
        string captures = Path.Combine(outputFolder, "captures");
        void Step(string name) => context.Step(name);

        var mail = (MailWindow)PublishedHost.Ensure("mail");
        OffscreenWindow.ShowInactive(mail);
        await WaitUntilLoadedAsync(mail);
        // A view that the walk does not have is answered with the ones it
        // has, and nothing is walked.
        string? onlyView = context.TargetView;
        if (onlyView is not null && !MailWindow.CaptureSlugs.Contains(onlyView))
            report.Views = [.. MailWindow.CaptureSlugs];
        else await mail.WalkCapturesAsync(
            async visit =>
            {
                // A menu, flyout or dialog left open on purpose is the subject
                // of the step: the window is photographed with it, and the
                // walk closes it afterwards.
                if (visit.KeepsPopup)
                {
                    report.Surfaces.Add(await CaptureWindowFrameAsync(
                        run, mail, "mail", visit.Slug, visit.Label, visit.Loaded, visit.LoadMs));
                    Step($"mail/{visit.Slug} done");
                    return;
                }
                (int closed, int left) = await CloseTransientPopupsAsync(mail);
                SelfCheckSurface surface = new("mail", visit.Slug, visit.Label, visit.Loaded, visit.LoadMs, null,
                    closed, left, "page-render");
                if (visit.Loaded && mail.CurrentPage is { } page)
                {
                    try
                    {
                        ElementImage.Result image = await ElementImage.CapturePageAsync(
                            page, Path.Combine(captures, $"mail-{visit.Slug}"));
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
                }
                report.Surfaces.Add(surface);
                if (visit.WholeWindow)
                    report.Surfaces.Add(await CaptureWindowFrameAsync(
                        run, mail, "mail", $"{visit.Slug}-window", visit.Label, visit.Loaded, visit.LoadMs));
                Step($"mail/{visit.Slug} done");
            },
            starting: (slug, label) => Step($"mail/{slug} ({label})"),
            only: onlyView);
    }
}
