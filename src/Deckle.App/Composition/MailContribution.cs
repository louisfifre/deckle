using Deckle.Contributions;
using Deckle.Mail.Hub;
using Deckle.Modules;

namespace Deckle.App;

// The Deckle Mail window, on its built-in sample until the engine is wired. A
// preview for the maintainer: it opens only under the developer setting, and
// no module or setting of its own registers it yet (that comes with the real
// engine), hence no descriptor.
internal sealed class MailContribution : IContribution
{
    public string Id => "mail";

    public ModuleDescriptor? Descriptor => null;

    public IReadOnlyList<ContributedWindow> Windows { get; } =
    [
        new ContributedWindow
        {
            Name = "mail",
            Create = () => new MailWindow(),
            Show = window => ((MailWindow)window).ShowAndActivate(),
            TrayLabelKey = "TrayMenu_Mail",
            DeveloperOnly = true,
            SetRecordingState = (window, isRecording) => ((MailWindow)window).SetRecordingState(isRecording),
        },
    ];

    // The views are the slugs of MailWindow.CaptureSlugs, read when the walk
    // runs: listing them here would load the window's type before a window is
    // asked for.
    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; } =
    [
        new ContributedWalk
        {
            WindowName = "mail",
            ShowsViewsAtRun = true,
            Run = context =>
            {
                SelfCheckWalkContext walk = SelfCheckWalkContext.From(context);
                return walk.App.WalkMailAsync(walk);
            },
        },
    ];

    public void Compose(IContributionContext context)
    {
        // Nothing to plant: the window reaches nothing outside itself.
    }
}
