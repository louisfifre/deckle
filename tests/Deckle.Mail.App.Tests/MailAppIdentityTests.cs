using Deckle.Contributions;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Deckle.Mail.App.Tests;

// What keeps Deckle Mail apart from the published Deckle: its identity decides
// the data root, the log folder and the resident mutex, and its composition
// carries Mail alone.
[Trait("Category", "unit")]
public sealed class MailAppIdentityTests
{
    [Fact]
    public void TheAppHasItsOwnResidentMutex()
    {
        AppIdentity identity = AppIdentity.Create(Program.AppId);

        Assert.Equal(@"Local\Deckle.Mail.Resident", identity.ResidentMutexName);
        Assert.NotEqual(AppIdentity.Suite.ResidentMutexName, identity.ResidentMutexName);
    }

    [Fact]
    public void TheAppKeepsItsDataBelowTheSuiteRootAndItsLogInASubfolder()
    {
        AppRoots roots = AppRoots.For(AppIdentity.Create(Program.AppId), @"C:\suite");

        Assert.Equal(@"C:\suite\apps\mail", roots.UserDataRoot);
        Assert.Equal(@"C:\suite\diagnostics\mail", roots.DiagnosticsDirectory);
    }

    [Fact]
    public void TheCompositionCarriesMailAndItsWalkAlone()
    {
        Assert.Equal(["mail"], Composition.All.Select(c => c.Id));
        ContributedWalk walk = Assert.Single(Composition.Walks);
        Assert.Equal("mail", walk.WindowName);
        Assert.True(walk.HasViews);
    }

    [Fact]
    public void TheCompositionIsAcceptedByTheHost()
    {
        var host = new ContributionHost(Composition.All, new NoWindowServices());

        Assert.Equal(["mail"], host.WindowNames);
        Assert.Empty(host.Descriptors);
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
