using Xunit;

namespace Deckle.Settings.Tests;

// The settings backup took the shell settings.json alone, while each module
// keeps its own modules/<id>/settings.json. A restore then brought back the
// shell and left every module on its current values. These tests pin that a
// snapshot carries the module files and that a restore puts them back.
[Trait("Category", "regression")]
public sealed class SettingsBackupRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "deckle-backup-tests-" + Guid.NewGuid().ToString("N"));

    private string Shell => Path.Combine(_root, "settings.json");
    private string Modules => Path.Combine(_root, "modules");
    private string Backups => Path.Combine(_root, "backups");

    private string ModuleFile(string id) => Path.Combine(Modules, id, "settings.json");

    private void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void ABackupCarriesEveryModuleSettingsFileAndRestoreBringsThemBack()
    {
        Write(Shell, """{"theme":"dark"}""");
        Write(ModuleFile("audio"), """{"gain":3}""");
        Write(ModuleFile("hud"), """{"opacity":0.5}""");

        BackupInfo? backup = SettingsBackupService.CreateSnapshot(Shell, Modules, Backups, DateTimeOffset.Now);
        Assert.NotNull(backup);

        File.WriteAllText(Shell, """{"theme":"light"}""");
        File.WriteAllText(ModuleFile("audio"), """{"gain":9}""");
        File.Delete(ModuleFile("hud"));

        Assert.True(SettingsBackupService.RestoreSnapshot(backup!.Path, Shell, Modules, out bool shellReplaced));

        Assert.True(shellReplaced);
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(Shell));
        Assert.Equal("""{"gain":3}""", File.ReadAllText(ModuleFile("audio")));
        Assert.Equal("""{"opacity":0.5}""", File.ReadAllText(ModuleFile("hud")));
    }

    [Fact]
    public void ABackupFromAnOlderVersionRestoresTheShellFileAndLeavesModulesAlone()
    {
        Write(Shell, """{"theme":"light"}""");
        Write(ModuleFile("audio"), """{"gain":9}""");
        string flat = Path.Combine(Backups, "settings-20200101-000000.json");
        Write(flat, """{"theme":"dark"}""");

        Assert.True(SettingsBackupService.RestoreSnapshot(flat, Shell, Modules, out _));

        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(Shell));
        Assert.Equal("""{"gain":9}""", File.ReadAllText(ModuleFile("audio")));
    }

    [Fact]
    public void ARestoreWithOneInvalidModuleFileChangesNothing()
    {
        Write(Shell, """{"theme":"dark"}""");
        Write(ModuleFile("audio"), """{"gain":3}""");
        BackupInfo backup = SettingsBackupService.CreateSnapshot(Shell, Modules, Backups, DateTimeOffset.Now)!;
        File.WriteAllText(Path.Combine(backup.Path, "modules", "audio", "settings.json"), "{ not json");

        File.WriteAllText(Shell, """{"theme":"light"}""");
        File.WriteAllText(ModuleFile("audio"), """{"gain":9}""");

        Assert.False(SettingsBackupService.RestoreSnapshot(backup.Path, Shell, Modules, out bool shellReplaced));

        Assert.False(shellReplaced);
        Assert.Equal("""{"theme":"light"}""", File.ReadAllText(Shell));
        Assert.Equal("""{"gain":9}""", File.ReadAllText(ModuleFile("audio")));
    }

    [Fact]
    public void AHalfBuiltSnapshotIsNotOffered()
    {
        Write(Shell, "{}");
        BackupInfo backup = SettingsBackupService.CreateSnapshot(Shell, Modules, Backups, DateTimeOffset.Now)!;
        Directory.CreateDirectory(backup.Path + ".partial");
        File.WriteAllText(Path.Combine(backup.Path + ".partial", "settings.json"), "{}");

        Assert.Equal(backup.Path, Assert.Single(SettingsBackupService.ListSnapshots(Backups)).Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

