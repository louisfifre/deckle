using Deckle.Hosting;
using Deckle.Contributions;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Modules;

namespace Deckle.App;

// The contributed windows (PublishedComposition): the host runner that creates
// them, places them, themes them and tells them the recording state, and the
// door through which it asks this application for the window mechanics it
// does not own. The windows are handled here exactly as the application's own
// (App.Windows.cs): built on first use through the logging constructor,
// placed where the person left them, dropped when closed.
public partial class App
{
    private ContributionHost? _publishedHost;

    // The names the application's own windows use, which a contribution may
    // not take (they are image names and placement keys too).
    private static readonly string[] HostWindowNames =
        ["settings", "log", "hud", "transcribe-rewrite", "paste-to-folder"];

    // The host of the composition, built on first use: a wrong composition
    // refuses the start there, before any window exists.
    internal static ContributionHost PublishedHost => ((App)Current).EnsurePublishedHost();

    private ContributionHost EnsurePublishedHost()
    {
        if (_publishedHost is null)
        {
            var host = new ContributionHost(PublishedComposition.All, new ContributionServices(), HostWindowNames);
            // A window opened later starts from the state the recording is in.
            host.SetRecordingState(_lastRecordingState);
            _publishedHost = host;
        }
        return _publishedHost;
    }

    // What the host runner asks of this application.
    private sealed class ContributionServices : IContributionHostServices
    {
        public Microsoft.UI.Xaml.Window CreateOrLog(string name, Func<Microsoft.UI.Xaml.Window> create)
            => WindowLifecycle.CreateOrLog(name, create);

        public void Place(Microsoft.UI.Xaml.Window window, string name)
        {
            WindowPlacement.Restore(window, name);
            window.AppWindow.Closing += (_, _) => WindowPlacement.Save(window, name);
        }

        public void ApplyTheme(Microsoft.UI.Xaml.Window window) => WindowTheme.Apply(window);

        public void WhenClosed(Microsoft.UI.Xaml.Window window, Action closed)
            => window.Closed += (_, _) => closed();

        public void WindowOpened(string name) => DeckleAppSource.Log.WindowOpened(name);

        public string ModuleDirectory(string moduleId) => AppPaths.GetModuleDirectory(moduleId);

        public bool IsModulePresent(string moduleId) => ModulePresence.IsPresent(moduleId);

        public bool DeveloperModeEnabled => App.DeveloperModeEnabled;

        public void OpenSettingsPage(string pageTag) => ((App)Current).ShowSettingsWindowLazy(pageTag);
    }
}
