using Deckle.Contributions;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Deckle.Projects.App.Tests;

// What keeps Deckle Projects apart from the published Deckle: its identity
// decides the data root, the log folder and the resident mutex, and its
// composition carries Projects alone.
[Trait("Category", "unit")]
public sealed class ProjectsAppIdentityTests
{
    [Fact]
    public void TheAppHasItsOwnResidentMutex()
    {
        AppIdentity identity = AppIdentity.Create(Program.AppId);

        Assert.Equal(@"Local\Deckle.Projects.Resident", identity.ResidentMutexName);
        Assert.NotEqual(AppIdentity.Suite.ResidentMutexName, identity.ResidentMutexName);
    }

    [Fact]
    public void TheAppKeepsItsDataBelowTheSuiteRootAndItsLogInASubfolder()
    {
        AppRoots roots = AppRoots.For(AppIdentity.Create(Program.AppId), @"C:\Code\suite");

        Assert.Equal(@"C:\Code\suite\apps\projects", roots.UserDataRoot);
        Assert.Equal(@"C:\Code\suite\diagnostics\projects", roots.DiagnosticsDirectory);
    }

    [Fact]
    public void TheCompositionCarriesProjectsAlone()
    {
        Assert.Equal(["projects"], Composition.All.Select(c => c.Id));
    }

    [Fact]
    public void TheCompositionIsAcceptedByTheHost()
    {
        var host = new ContributionHost(Composition.All, new NoWindowServices());

        Assert.Equal(["projects"], host.WindowNames);
        Assert.Empty(host.Descriptors);
    }

    // The window's self-check walk left the published Deckle with the window:
    // this app's self-check names it "projects", with several states, so
    // `--only projects/p1` reaches it as it did in Deckle.exe.
    [Fact]
    public void TheSelfCheckWalksTheProjectsWindowWithItsStates()
    {
        ContributedWalk walk = Assert.Single(Composition.Walks);

        Assert.Equal("projects", walk.WindowName);
        Assert.True(walk.HasViews);
    }

    [Fact]
    public void NoActionRunsBeforeTheWindowWasBuilt()
    {
        Assert.Equal(0, new ProjectsContribution().RunsGoingOn);
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
