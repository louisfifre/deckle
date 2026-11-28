using Deckle.Browser.Views;
using Deckle.Contributions;
using Deckle.Modules;

namespace Deckle.Browser.App;

// The Deckle Browser window. It lives in its own app, so the published Deckle
// carries nothing of it, and no module or setting of its own registers it
// yet, hence no descriptor and no tray entry.
internal sealed class BrowserContribution : IContribution
{
    public string Id => "browser";

    public ModuleDescriptor? Descriptor => null;

    public IReadOnlyList<ContributedWindow> Windows { get; } =
    [
        new ContributedWindow
        {
            Name = "browser",
            Create = () => new BrowserWindow(),
            Show = window => ((BrowserWindow)window).ShowAndActivate(),
        },
    ];

    // The views are the images of BrowserSelfCheck: the window with the
    // panel open and in the rail, the page as XAML renders it, and the page as
    // the web view draws it.
    public IReadOnlyList<ContributedWalk> SelfCheckWalks { get; } =
    [
        new ContributedWalk
        {
            WindowName = "browser",
            Views = BrowserSelfCheck.Slugs,
            Run = context => BrowserSelfCheck.WalkAsync(BrowserWalkContext.From(context)),
        },
    ];

    public void Compose(IContributionContext context)
    {
        // Nothing to plant: the window reaches nothing outside itself.
    }
}
