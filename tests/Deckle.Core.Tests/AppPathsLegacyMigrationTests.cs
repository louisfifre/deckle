using Xunit;

namespace Deckle.Core.Tests;

// The legacy settings layout is moved from AppPaths' static constructor, before
// any log sink exists. What these tests pin is that a move which fails comes
// back as the error, so the host can report it, instead of vanishing.
[Trait("Category", "integration")]
public sealed class AppPathsLegacyMigrationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"deckle-paths-{Guid.NewGuid():N}");

    private string SettingsFile => Path.Combine(_root, "settings.json");

    private string BackupDirectory => Path.Combine(_root, "backups");

    [Fact]
    public void NothingToMigrateIsNotAFailure()
    {
        Directory.CreateDirectory(_root);

        Assert.Null(AppPaths.MigrateLegacySettingsLayout(_root, SettingsFile, BackupDirectory));
    }

    [Fact]
    public void ALegacyLayoutMovesToTheRootAndLeavesNoEmptyFolderBehind()
    {
        string legacy = Path.Combine(_root, "settings");
        Directory.CreateDirectory(Path.Combine(legacy, "backups"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

        Exception? failure = AppPaths.MigrateLegacySettingsLayout(_root, SettingsFile, BackupDirectory);

        Assert.Null(failure);
        Assert.True(File.Exists(SettingsFile));
        Assert.True(Directory.Exists(BackupDirectory));
        Assert.False(Directory.Exists(legacy));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}

// A failed move used to vanish in a bare catch, so settings left in the old
// folder read as defaults with nothing in the log to say why.
[Trait("Category", "regression")]
public sealed class AppPathsLegacyMigrationRegressionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"deckle-paths-{Guid.NewGuid():N}");

    [Fact]
    public void AMoveThatFailsIsReturnedInsteadOfSwallowed()
    {
        string legacy = Path.Combine(_root, "settings");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
        // The destination folder does not exist, so the move itself throws.
        string unreachable = Path.Combine(_root, "missing-folder", "settings.json");

        Exception? failure = AppPaths.MigrateLegacySettingsLayout(
            _root, unreachable, Path.Combine(_root, "backups"));

        Assert.IsType<DirectoryNotFoundException>(failure);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
