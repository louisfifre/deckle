using Deckle.Contributions;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Deckle.Browser.App.Tests;

// What keeps Deckle Browser apart from the published Deckle: its identity
// decides the data root, the log folder and the resident mutex, and its
// composition carries the Browser alone.
[Trait("Category", "unit")]
public sealed class BrowserAppIdentityTests
{
    [Fact]
    public void TheAppHasItsOwnResidentMutex()
    {
        AppIdentity identity = AppIdentity.Create(Program.AppId);

        Assert.Equal(@"Local\Deckle.Browser.Resident", identity.ResidentMutexName);
        Assert.NotEqual(AppIdentity.Suite.ResidentMutexName, identity.ResidentMutexName);
    }

    [Fact]
    public void TheAppKeepsItsDataBelowTheSuiteRootAndItsLogInASubfolder()
    {
        AppRoots roots = AppRoots.For(AppIdentity.Create(Program.AppId), @"C:\Code\suite");

        Assert.Equal(@"C:\Code\suite\apps\browser", roots.UserDataRoot);
        Assert.Equal(@"C:\Code\suite\diagnostics\browser", roots.DiagnosticsDirectory);
    }

    [Fact]
    public void TheCompositionCarriesTheBrowserAlone()
    {
        Assert.Equal(["browser"], Composition.All.Select(c => c.Id));
    }

    [Fact]
    public void TheCompositionIsAcceptedByTheHost()
    {
        var host = new ContributionHost(Composition.All, new NoWindowServices());

        Assert.Equal(["browser"], host.WindowNames);
        Assert.Empty(host.Descriptors);
    }

    // `--only browser/page-xaml` reaches a view of the walk, as `--only mail/...`
    // does in Deckle Mail.
    [Fact]
    public void TheSelfCheckWalksTheBrowserWindowWithItsViews()
    {
        ContributedWalk walk = Assert.Single(Composition.Walks);

        Assert.Equal("browser", walk.WindowName);
        Assert.True(walk.HasViews);
        Assert.Contains("page-preview", walk.Views);
    }

    // The host builds no window until one is asked for, so none of these runs.
    private sealed class NoWindowServices : IContributionHostServices
    {
        public Window CreateOrLog(string name, Func<Window> create) => throw new NotSupportedException();
        public void Place(Window window, string name) => throw new NotSupportedException();
        public void ApplyTheme(Window window) => throw new NotSupportedException();
        public void WhenClosed(Window window, Action closed) => throw new NotSupportedException();
        public void WindowOpened(string name) { }
        public string ModuleDirectory(string moduleId) => throw new NotSupportedException();
        public bool IsModulePresent(string moduleId) => true;
        public bool DeveloperModeEnabled => true;
        public void OpenSettingsPage(string pageTag) { }
    }
}
