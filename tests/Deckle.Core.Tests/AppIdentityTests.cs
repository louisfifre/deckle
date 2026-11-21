using Deckle.Core;
using Xunit;

namespace Deckle.Core.Tests;

// Pins what an app's identity decides: where its data and its logs live, and
// the name of its resident mutex. AppPaths applies AppRoots to the real root
// once per process, so the rule is tested here on strings.
public class AppIdentityTests
{
    private const string SuiteRoot = @"C:\Users\someone\AppData\Local\Deckle";

    [Fact]
    [Trait("Category", "unit")]
    public void SuiteApp_KeepsTheSuiteRootAndItsDiagnosticsFolder()
    {
        AppRoots roots = AppRoots.For(AppIdentity.Suite, SuiteRoot);

        Assert.Equal(SuiteRoot, roots.UserDataRoot);
        Assert.Equal(SuiteRoot + @"\diagnostics", roots.DiagnosticsDirectory);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void OtherApp_HasItsOwnDataRootAndItsOwnLogSubfolder()
    {
        AppRoots roots = AppRoots.For(AppIdentity.Create("mail"), SuiteRoot);

        Assert.Equal(SuiteRoot + @"\apps\mail", roots.UserDataRoot);
        Assert.Equal(SuiteRoot + @"\diagnostics\mail", roots.DiagnosticsDirectory);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void TwoApps_NeverShareARootOrALogFolder()
    {
        AppRoots mail = AppRoots.For(AppIdentity.Create("mail"), SuiteRoot);
        AppRoots atelier = AppRoots.For(AppIdentity.Create("atelier"), SuiteRoot);
        AppRoots suite = AppRoots.For(AppIdentity.Suite, SuiteRoot);

        string[] data = [mail.UserDataRoot, atelier.UserDataRoot, suite.UserDataRoot];
        string[] logs = [mail.DiagnosticsDirectory, atelier.DiagnosticsDirectory, suite.DiagnosticsDirectory];
        Assert.Equal(3, data.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, logs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // The published Deckle's mutex is the name its single-instance guard has
    // always used; a change would let a new build run beside an old one.
    [Fact]
    [Trait("Category", "regression")]
    public void SuiteApp_KeepsTheResidentMutexNameOfTheDeckleExe()
    {
        Assert.Equal(@"Local\Deckle.Resident", AppIdentity.Suite.ResidentMutexName);
    }

    [Theory]
    [InlineData("mail", @"Local\Deckle.Mail.Resident")]
    [InlineData("atelier", @"Local\Deckle.Atelier.Resident")]
    [InlineData("folder-map", @"Local\Deckle.FolderMap.Resident")]
    [Trait("Category", "unit")]
    public void OtherApp_HasAResidentMutexOfItsOwn(string id, string expected)
    {
        Assert.Equal(expected, AppIdentity.Create(id).ResidentMutexName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Mail")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("-mail")]
    [InlineData("mail-")]
    [InlineData("..")]
    [Trait("Category", "unit")]
    public void Create_RefusesWhatIsNotAFolderAndMutexSafeId(string id)
    {
        Assert.Throws<ArgumentException>(() => AppIdentity.Create(id));
    }

    [Fact]
    [Trait("Category", "unit")]
    public void Create_OfTheSuiteIdGivesTheSuiteIdentity()
    {
        Assert.Same(AppIdentity.Suite, AppIdentity.Create("deckle"));
    }
}
