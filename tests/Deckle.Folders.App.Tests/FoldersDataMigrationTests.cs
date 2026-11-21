using Xunit;

namespace Deckle.Folders.App.Tests;

// The one-time move of the data the app kept under its old code name. Each
// test builds a suite root in a temp folder and reads what the move left.
[Trait("Category", "unit")]
public sealed class FoldersDataMigrationTests : IDisposable
{
    private readonly string _suite = Path.Combine(Path.GetTempPath(), "deckle-folders-migration-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_suite)) Directory.Delete(_suite, recursive: true);
    }

    private string Apps(string id, params string[] rest) => Path.Combine([_suite, "apps", id, .. rest]);

    private void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void AnEmptySuiteRootHasNothingToMove()
    {
        Directory.CreateDirectory(_suite);

        FoldersDataMigrationResult result = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.NothingToMove, result.Outcome);
        Assert.False(Directory.Exists(Apps("folders")));
    }

    [Fact]
    public void TheOldDataRootMovesAndItsModuleFolderIsRenamed()
    {
        Write(Apps("atelier", "settings.json"), "root");
        Write(Apps("atelier", "modules", "atelier", "photo-description-model.json"), "model");
        Write(Apps("atelier", "modules", "other", "keep.txt"), "other");

        FoldersDataMigrationResult result = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.Moved, result.Outcome);
        Assert.False(Directory.Exists(Apps("atelier")));
        Assert.Equal("root", File.ReadAllText(Apps("folders", "settings.json")));
        Assert.Equal("model", File.ReadAllText(Apps("folders", "modules", "folders", "photo-description-model.json")));
        Assert.False(Directory.Exists(Apps("folders", "modules", "atelier")));
        Assert.Equal("other", File.ReadAllText(Apps("folders", "modules", "other", "keep.txt")));
    }

    [Fact]
    public void ADataRootWithoutAModuleFolderMovesAlone()
    {
        Write(Apps("atelier", "settings.json"), "root");

        FoldersDataMigrationResult result = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.Moved, result.Outcome);
        Assert.Equal("root", File.ReadAllText(Apps("folders", "settings.json")));
    }

    [Fact]
    public void WhenBothRootsExistNothingMoves()
    {
        Write(Apps("atelier", "settings.json"), "old");
        Write(Apps("folders", "settings.json"), "new");

        FoldersDataMigrationResult result = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.BothExist, result.Outcome);
        Assert.Equal("old", File.ReadAllText(Apps("atelier", "settings.json")));
        Assert.Equal("new", File.ReadAllText(Apps("folders", "settings.json")));
    }

    [Fact]
    public void AModuleFolderAlreadyNamedFoldersIsNeverOverwritten()
    {
        Write(Apps("atelier", "modules", "atelier", "a.json"), "old");
        Write(Apps("atelier", "modules", "folders", "b.json"), "own");

        FoldersDataMigrationResult result = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.MovedModuleFolderKept, result.Outcome);
        Assert.Equal("old", File.ReadAllText(Apps("folders", "modules", "atelier", "a.json")));
        Assert.Equal("own", File.ReadAllText(Apps("folders", "modules", "folders", "b.json")));
    }

    [Fact]
    public void ARunAfterTheMoveDoesNothing()
    {
        Write(Apps("atelier", "settings.json"), "root");
        FoldersDataMigration.Run(_suite);

        FoldersDataMigrationResult second = FoldersDataMigration.Run(_suite);

        Assert.Equal(FoldersDataMigrationOutcome.NothingToMove, second.Outcome);
        Assert.Equal("root", File.ReadAllText(Apps("folders", "settings.json")));
    }
}
