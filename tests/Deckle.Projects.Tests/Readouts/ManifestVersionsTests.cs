using Xunit;

namespace Deckle.Projects.Tests;

// Which manifests are looked at and how many are listed, on a scratch folder.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ManifestVersionsTests
{
    [Fact]
    public void TheMinecraftShapeOfP2ListsItsSourcesInPathOrder()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("voxel-mods");
        scratch.File("[package]\nname = \"launcher\"\nversion = \"0.1.0\"\n", "voxel-mods", "launcher", "app", "Cargo.toml");
        scratch.File("mod_id=voice\nmod_version=0.1.0\n", "voxel-mods", "mods", "voice", "gradle.properties");

        IReadOnlyList<string> candidates = ManifestVersions.Walk(root);
        ManifestScan scan = ManifestVersions.Read(root, candidates);

        Assert.Equal(["launcher/app/Cargo.toml", "mods/voice/gradle.properties"], scan.Sources.Select(s => s.Label));
        Assert.Equal(["0.1.0", "0.1.0"], scan.Sources.Select(s => s.Value));
        Assert.Equal(0, scan.More);
    }

    [Fact]
    public void TheRootsManifestLeadsTheDeeperOnes()
    {
        IReadOnlyList<string> ordered = ManifestVersions.Candidates(
            ["z/package.json", "a/b/Cargo.toml", "package.json", "a/package.json", "README.md"]);

        Assert.Equal(["package.json", "a/package.json", "z/package.json", "a/b/Cargo.toml"], ordered);
    }

    [Fact]
    public void FoldersThatOnlyHoldBuildOutputAreNotLookedInto()
    {
        IReadOnlyList<string> ordered = ManifestVersions.Candidates(
            ["node_modules/x/package.json", "target/debug/Cargo.toml", "src/app/package.json", "bin/Debug/App.csproj"]);

        Assert.Equal(["src/app/package.json"], ordered);
    }

    [Fact]
    public void TheWalkSkipsBuildFoldersAndStopsAtDepthFour()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("p");
        scratch.File("{\"version\":\"1.0.0\"}", "p", "package.json");
        scratch.File("{\"version\":\"2.0.0\"}", "p", "node_modules", "x", "package.json");
        scratch.File("{\"version\":\"3.0.0\"}", "p", "a", "b", "c", "d", "package.json");
        scratch.File("{\"version\":\"4.0.0\"}", "p", "a", "b", "c", "d", "e", "package.json");

        IReadOnlyList<string> found = ManifestVersions.Walk(root);

        Assert.Equal(["package.json", "a/b/c/d/package.json"], found);
    }

    [Fact]
    public void ThirtyPackageJsonFilesListTwentyAndSayTenMore()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("mono");
        for (int i = 0; i < 30; i++)
            scratch.File($"{{\"version\":\"1.{i}.0\"}}", "mono", $"pkg{i:00}", "package.json");

        ManifestScan scan = ManifestVersions.Read(root, ManifestVersions.Walk(root));

        Assert.Equal(20, scan.Sources.Count);
        Assert.Equal(10, scan.More);
    }

    [Fact]
    public void AManifestWithoutAVersionOrOverAMebibyteOrBrokenGivesNoSource()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("p");
        scratch.File("{\"name\":\"a\"}", "p", "a", "package.json");
        scratch.File("{\"version\":\"1.0.0\",\"pad\":\"" + new string('x', 1024 * 1024) + "\"}", "p", "b", "package.json");
        scratch.File("{ not json", "p", "c", "package.json");
        scratch.File("[package]\nversion = \"7.0.0\"\n", "p", "d", "Cargo.toml");

        ManifestScan scan = ManifestVersions.Read(root, ManifestVersions.Walk(root));

        Assert.Equal(["d/Cargo.toml"], scan.Sources.Select(s => s.Label));
    }

    [Fact]
    public void ABrokenManifestSaysOneVerboseEventNamingItsFormatAndNothingElse()
    {
        using var scratch = new ScratchFolder();
        string root = scratch.Folder("p");
        scratch.File("{ not json", "p", "package.json");
        using var listener = new Deckle.TestSupport.TestEventListener("Deckle-Projects");

        ManifestVersions.Read(root, ["package.json"]);

        var written = Assert.Single(listener.Events, e => e.EventId == DeckleProjectsSource.EvtManifestUnreadable);
        Assert.Equal("package.json", written.Payload?[0]);
    }
}
