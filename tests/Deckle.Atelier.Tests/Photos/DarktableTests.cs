using Microsoft.Win32;
using Xunit;

namespace Deckle.Atelier.Tests;

// darktable is found where its installer puts it, and opened with the folder
// as one argument.
[Trait("Category", "unit")]
public sealed class DarktableTests
{
    private const string UserExe = @"C:\Users\u\AppData\Local\Programs\darktable\bin\darktable.exe";
    private const string MachineExe = @"C:\Program Files\darktable\bin\darktable.exe";

    [Fact]
    public void ThePerUserAppPathComesFirst()
    {
        DarktableInstall? found = Darktable.Find(
            hive => hive == RegistryHive.CurrentUser ? $"\"{UserExe}\"" : MachineExe,
            _ => true,
            Folders);

        Assert.Equal(new DarktableInstall(UserExe, DarktableSource.AppPathsUser), found);
    }

    [Fact]
    public void AnAppPathToAMissingFileIsPassedOver()
    {
        DarktableInstall? found = Darktable.Find(
            hive => hive == RegistryHive.CurrentUser ? UserExe : MachineExe,
            path => path == MachineExe,
            Folders);

        Assert.Equal(DarktableSource.AppPathsMachine, found?.FoundBy);
    }

    [Fact]
    public void WithoutAppPathsTheInstallerFoldersAreTried()
    {
        DarktableInstall? found = Darktable.Find(_ => null, path => path == UserExe, Folders);

        Assert.Equal(new DarktableInstall(UserExe, DarktableSource.UserPrograms), found);
    }

    [Fact]
    public void NothingFoundIsNull() =>
        Assert.Null(Darktable.Find(_ => null, _ => false, Folders));

    [Fact]
    public void TheFolderIsOneArgumentWhateverItHolds()
    {
        var install = new DarktableInstall(MachineExe, DarktableSource.ProgramFiles);

        var start = Darktable.OpenFolderCommand(install, @"D:\Photos\2026\2026-06-14 Mariage\");

        Assert.Equal(MachineExe, start.FileName);
        Assert.Equal([@"D:\Photos\2026\2026-06-14 Mariage"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    private static string Folders(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.ProgramFiles => @"C:\Program Files",
        Environment.SpecialFolder.LocalApplicationData => @"C:\Users\u\AppData\Local",
        _ => "",
    };
}
