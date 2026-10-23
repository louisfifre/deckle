using System.IO;
using Deckle.Security;
using Xunit;

namespace Deckle.Security.Tests;

// The path a vault or credentials message names is masked, so the message can go to
// the log and the screen without the Windows user name in it.
[Trait("Category", "unit")]
public sealed class UserProfilePathTests
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void APathUnderLocalAppDataIsNamedByItsVariable()
    {
        string path = Path.Combine(LocalAppData, "Deckle", "secrets.dat");

        Assert.Equal(@"%LOCALAPPDATA%\Deckle\secrets.dat", UserProfilePath.Mask(path));
    }

    [Fact]
    public void APathUnderTheProfileOutsideLocalAppDataIsNamedByTheProfileVariable()
    {
        string path = Path.Combine(UserProfile, "Documents", "notes.txt");

        Assert.Equal(@"%USERPROFILE%\Documents\notes.txt", UserProfilePath.Mask(path));
    }

    [Fact]
    public void APathOutsideTheProfileIsLeftAsItIs()
    {
        string path = Path.Combine(Path.GetPathRoot(UserProfile)!, "ProgramData", "x.dat");

        Assert.Equal(path, UserProfilePath.Mask(path));
    }

    [Fact]
    public void AFolderThatOnlySharesAPrefixWithTheProfileIsNotMasked()
    {
        string path = UserProfile + "-other" + Path.DirectorySeparatorChar + "x.dat";

        Assert.Equal(path, UserProfilePath.Mask(path));
    }

    [Fact]
    [Trait("Category", "regression")]
    public void AnUnreadableVaultNamesItsFileWithoutTheUserName()
    {
        string dir = Path.Combine(LocalAppData, "deckle-vault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "secrets.dat");
            File.WriteAllBytes(file, [0x00, 0x01, 0x02, 0x03, 0x04]);

            SecretVaultException error = Assert.Throws<SecretVaultException>(
                () => new SecretVault(file).TryGet("k", out _));

            Assert.Contains("%LOCALAPPDATA%", error.Message);
            Assert.DoesNotContain(UserProfile, error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}