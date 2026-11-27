using Xunit;

namespace Deckle.Projects.Tests;

// The reader against a real repository made by the git command line in a
// scratch folder (the integration category: a local partner the test stands up
// itself). Each test skips with its reason on a machine without git.
[Collection(ProjectsProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class ProjectReaderGitTests
{
    private static ProjectReader NewReader() => new();

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task ARepositoryWithTwoTagsAnUntrackedAndAModifiedFileReadsAllOfIt()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "alpha");
        Tools.Git(repo, "tag", "v0.9.0");
        File.WriteAllText(Path.Combine(repo, "README.md"), "changed\n");
        Tools.Git(repo, "commit", "-q", "-a", "-m", "second");
        Tools.Git(repo, "tag", "v0.131.0");
        File.WriteAllText(Path.Combine(repo, "README.md"), "changed again\n");
        File.WriteAllText(Path.Combine(repo, "new file.txt"), "x");
        File.WriteAllText(Path.Combine(repo, "écrit.txt"), "x");

        ProjectReading reading = await NewReader().ReadAsync(repo, CancellationToken.None);

        Assert.Equal(FolderState.Present, reading.Folder);
        Assert.True(reading.IsGitRepository);
        Assert.Equal("v0.131.0", reading.Version.Tag);
        Assert.Equal(3, reading.UncommittedFiles);
        Assert.Null(reading.Remote);
        Assert.False(reading.HasScriptMenu);
        Assert.Equal(VersionSource.GitTagLabel, reading.Version.Sources[0].Label);
        Assert.Equal("0.131.0", reading.Version.Sources[0].Value);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task ACleanRepositoryWithNoTagHasNoVersionAndNothingUncommitted()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "alpha");

        ProjectReading reading = await NewReader().ReadAsync(repo, CancellationToken.None);

        Assert.Null(reading.Version.Tag);
        Assert.Equal(0, reading.UncommittedFiles);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task TheRemoteComesFromOriginAndGivesAWebPage()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "alpha");
        Tools.Git(repo, "remote", "add", "origin", "https://github.com/sample/alpha.git");

        ProjectReading reading = await NewReader().ReadAsync(repo, CancellationToken.None);

        Assert.Equal("GitHub", reading.Remote!.HostName);
        Assert.Equal("https://github.com/sample/alpha", reading.Remote.WebPage.ToString());
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task ASubfolderProjectCountsOnlyItsOwnChanges()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "mono");
        Directory.CreateDirectory(Path.Combine(repo, "app"));
        Directory.CreateDirectory(Path.Combine(repo, "lib"));
        File.WriteAllText(Path.Combine(repo, "app", "a.txt"), "x");
        File.WriteAllText(Path.Combine(repo, "lib", "b.txt"), "x");
        File.WriteAllText(Path.Combine(repo, "lib", "c.txt"), "x");

        ProjectReading app = await NewReader().ReadAsync(Path.Combine(repo, "app"), CancellationToken.None);
        ProjectReading lib = await NewReader().ReadAsync(Path.Combine(repo, "lib"), CancellationToken.None);

        Assert.Equal(1, app.UncommittedFiles);
        Assert.Equal(2, lib.UncommittedFiles);
        Assert.True(app.IsGitRepository);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task ManifestsAreReadFromTheFilesGitListsAndIgnoredFoldersAreSkipped()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "mods");
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "ignored/\n");
        Directory.CreateDirectory(Path.Combine(repo, "launcher", "app"));
        Directory.CreateDirectory(Path.Combine(repo, "ignored"));
        File.WriteAllText(Path.Combine(repo, "launcher", "app", "Cargo.toml"), "[package]\nversion = \"0.1.0\"\n");
        File.WriteAllText(Path.Combine(repo, "ignored", "package.json"), "{\"version\":\"9.9.9\"}");
        Tools.Git(repo, "tag", "0.32.0");

        ProjectReading reading = await NewReader().ReadAsync(repo, CancellationToken.None);

        Assert.Equal("v0.32.0", reading.Version.Tag);
        Assert.Equal([VersionSource.GitTagLabel, "launcher/app/Cargo.toml"], reading.Version.Sources.Select(s => s.Label));
        Assert.True(reading.Version.HasDifferingVersions);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task AFolderThatIsNotARepositoryReadsAsSuchAndStillGivesItsManifests()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("plain");
        scratch.File("{\"version\":\"1.2.3\"}", "plain", "package.json");

        ProjectReading reading = await NewReader().ReadAsync(folder, CancellationToken.None);

        Assert.Equal(RepositoryState.NotARepository, reading.Git);
        Assert.False(reading.IsGitRepository);
        Assert.Null(reading.UncommittedFiles);
        Assert.Null(reading.Version.Tag);
        Assert.Equal("1.2.3", reading.Version.Sources.Single().Value);
        Assert.True(reading.Version.ShowsDetail);
    }

    [Fact]
    public async Task AMissingFolderReadsAsMissingAndNothingElse()
    {
        using var scratch = new ScratchFolder();

        ProjectReading reading = await NewReader().ReadAsync(Path.Combine(scratch.Path, "gone"), CancellationToken.None);

        Assert.Equal(FolderState.Missing, reading.Folder);
        Assert.Empty(reading.Version.Sources);
        Assert.Null(reading.UncommittedFiles);
        Assert.Null(reading.Remote);
        Assert.False(reading.HasScriptMenu);
    }

    [Fact]
    public async Task AFileWhereTheFolderWasReadsAsMissing()
    {
        using var scratch = new ScratchFolder();
        string file = scratch.File("x", "was-a-folder");

        ProjectReading reading = await NewReader().ReadAsync(file, CancellationToken.None);

        Assert.Equal(FolderState.Missing, reading.Folder);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task TheScriptMenuIsSeenWhenItsEntryFileIsThere()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "alpha");
        scratch.File("# stub", "alpha", "scripts", "menu.ps1");

        ProjectReading reading = await NewReader().ReadAsync(repo, CancellationToken.None);

        Assert.True(reading.HasScriptMenu);
    }

    [Fact(SkipUnless = nameof(Tools.GitPresent), SkipType = typeof(Tools), Skip = "git is not on the PATH")]
    public async Task ACommandThatOutlivesItsTimeLimitIsKilledAndReadsAsTimedOut()
    {
        using var scratch = new ScratchFolder();
        string repo = Tools.Repository(scratch, "alpha");
        // A limit no git process can meet: the result must say so and return.
        var git = new GitCommand(TimeSpan.FromMilliseconds(1));

        GitResult result = await git.RunAsync(repo, ["status"], CancellationToken.None);

        Assert.Equal(GitFailure.TimedOut, result.Failure);
    }
}
