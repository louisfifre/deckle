using Xunit;
using Deckle.Core;

namespace Deckle.Projects.Tests;

// The sample world builds the shapes it promises, which the self-check
// photographs: counts, tags, the manifest triple of P2, the estimates of P1 and
// P2. Real repositories are made with git, so these skip without it.
[Collection("projects provider singleton")]
[Trait("Category", "integration")]
public sealed class ProjectsSampleWorldTests : IClassFixture<ProjectsSampleWorldTests.Built>
{
    // The world is built once for the class: nine folders and seven repositories
    // take a few seconds, and no test but the last one changes it.
    public sealed class Built : IDisposable
    {
        internal ScratchFolder Scratch { get; } = new();
        public ProjectsSample Sample { get; }

        public Built() => Sample = ProjectsSampleWorld.Create(Scratch.Path);

        public void Dispose() => Scratch.Dispose();
    }

    private readonly ScratchFolder _scratch;
    private readonly ProjectsSample _sample;
    private readonly ProjectReader _reader = new();

    public ProjectsSampleWorldTests(Built built)
    {
        _scratch = built.Scratch;
        _sample = built.Sample;
    }

    private Task<ProjectReading> Read(string name) => _reader.ReadAsync(_sample.PathOf(name), CancellationToken.None);

    [Fact]
    public void NineProjectsAreBuiltEachARealFolderUnderTheGivenRoot()
    {
        Assert.Equal(9, _sample.Projects.Count);
        Assert.All(_sample.Projects, p =>
        {
            Assert.True(Directory.Exists(p.Path));
            Assert.True(FolderPaths.IsInside(_scratch.Path, p.Path));
        });
    }

    [Fact]
    public void NoNameInTheSampleIsARealProjectOfTheMaintainer()
    {
        string[] names = [.. _sample.Projects.Select(p => p.Name)];

        Assert.Equal(
            ["deckle", "scripts-kit", "home-server", "ui-toolkit", "voxel-mods", "terrain-preview", "city-mods", "personal-site", "tiny-notes"],
            names);
    }

    [Fact]
    public void PopulatingAStoreGivesTheTwoCategoriesInOrderAndTheMockupsCounts()
    {
        var store = new ProjectStore(Path.Combine(_scratch.Path, "data-" + Guid.NewGuid().ToString("N")));

        _sample.Populate(store);

        Assert.Equal(["Development", "Leisure"], store.Categories.Select(c => c.Name));
        Assert.Equal(4, store.ProjectsIn(store.Categories[0].Id).Count);
        Assert.Equal(3, store.ProjectsIn(store.Categories[1].Id).Count);
        Assert.Equal(["personal-site", "tiny-notes"], store.ProjectsIn(null).Select(p => p.DisplayName));
    }

    [Fact]
    public void TheTwoProjectsWithAScriptMenuHaveAMainActionAndDeckleTwoMorePins()
    {
        var store = new ProjectStore(Path.Combine(_scratch.Path, "data-" + Guid.NewGuid().ToString("N")));
        _sample.Populate(store);

        ProjectEntry deckle = store.FindByPath(_sample.PathOf("deckle"))!;
        ProjectEntry server = store.FindByPath(_sample.PathOf("home-server"))!;

        Assert.Equal("Build & run", deckle.MainAction!.Label);
        Assert.Equal(["Build & run", "Self-check", "Update prod"], deckle.Pinned.Select(p => p.Label));
        Assert.Equal("Debug", deckle.Pinned[1].Action.Variant);
        Assert.Equal("Server power", server.MainAction!.Label);
        Assert.Null(store.FindByPath(_sample.PathOf("scripts-kit"))!.Main);
    }

    [Fact]
    public async Task DeckleReadsLikeP1ABuildableRepositoryWithAnEstimateOfAbout1880Tokens()
    {
        Assert.SkipUnless(_sample.GitAvailable, "git is not on the PATH");

        ProjectReading reading = await Read("deckle");
        AgentEstimate? tokens = await AgentReading.EstimateAsync(_sample.PathOf("deckle"), CancellationToken.None);

        Assert.Equal("v0.131.0", reading.Version.Tag);
        Assert.Equal(3, reading.UncommittedFiles);
        Assert.Equal("v0.131.0 · 3 uncommitted", CardMeta.Text(reading));
        Assert.True(reading.HasScriptMenu);
        Assert.Equal("GitHub", reading.Remote!.HostName);
        Assert.False(reading.Version.ShowsDetail);
        Assert.Equal(ProjectsSampleWorld.DeckleAgentTokens, tokens!.Tokens);
    }

    [Fact]
    public async Task VoxelModsHasP2sThreeVersionsAndNoScriptMenu()
    {
        Assert.SkipUnless(_sample.GitAvailable, "git is not on the PATH");

        ProjectReading reading = await Read("voxel-mods");
        AgentEstimate? tokens = await AgentReading.EstimateAsync(_sample.PathOf("voxel-mods"), CancellationToken.None);

        Assert.Equal("v0.32.0", reading.Version.Tag);
        Assert.Equal(3, reading.Version.Count);
        Assert.True(reading.Version.HasDifferingVersions);
        Assert.Equal(
            [VersionSource.GitTagLabel, "launcher/app/Cargo.toml", "mods/voice/gradle.properties"],
            reading.Version.Sources.Select(s => s.Label));
        Assert.Equal(["0.32.0", "0.1.0", "0.1.0"], reading.Version.Sources.Select(s => s.Value));
        Assert.Equal("v0.32.0 · 3 versions found", CardMeta.Text(reading));
        Assert.Equal(0, reading.UncommittedFiles);
        Assert.False(reading.HasScriptMenu);
        Assert.Null(reading.Remote);
        Assert.Equal(ProjectsSampleWorld.VoxelModsAgentTokens, tokens!.Tokens);
    }

    [Fact]
    public async Task TheOtherTagsCleanlinessAndTheMissingTagReadAsTheMockupDraws()
    {
        Assert.SkipUnless(_sample.GitAvailable, "git is not on the PATH");

        Assert.Equal("v0.6.0", CardMeta.Text(await Read("scripts-kit")));
        Assert.Equal("No version yet", CardMeta.Text(await Read("home-server")));
        Assert.Equal("v0.4.0", CardMeta.Text(await Read("ui-toolkit")));
        Assert.Equal("v0.19.0", CardMeta.Text(await Read("terrain-preview")));
        Assert.Equal("No version yet", CardMeta.Text(await Read("city-mods")));
        Assert.Equal("v0.1.0 · 2 uncommitted", CardMeta.Text(await Read("tiny-notes")));
    }

    [Fact]
    public async Task PersonalSiteIsNotARepositoryAndStillGivesItsManifestVersion()
    {
        Assert.SkipUnless(_sample.GitAvailable, "git is not on the PATH");

        ProjectReading reading = await Read("personal-site");

        Assert.Equal(RepositoryState.NotARepository, reading.Git);
        Assert.Equal("1.0.0", reading.Version.Sources.Single().Value);
        Assert.Equal("No version yet", CardMeta.Text(reading));
    }

    [Fact(SkipUnless = nameof(Tools.PwshPresent), SkipType = typeof(Tools), Skip = "pwsh is not on the PATH")]
    public async Task TheStubMenuOfDeckleAnswersListWithItsFourActionsAndVariants()
    {
        ScriptMenuListing listing = await new ScriptMenuReader(ShellLocator.Find())
            .ListAsync(_sample.PathOf("deckle"), TestContext.Current.CancellationToken);

        var listed = Assert.IsType<ScriptMenuListing.Listed>(listing);
        Assert.Equal(["build-run.1", "self-check.0", "deploy-prod.0"], listed.Entries.Select(e => e.Id));
        Assert.Equal(["Debug", "Release"], listed.Entries[1].Variants);
    }

    [Fact]
    public void BuildingTheSampleAgainReplacesTheOldOneEvenWithRepositoriesInIt()
    {
        string first = _sample.PathOf("deckle");
        File.WriteAllText(Path.Combine(first, "left-over.txt"), "x");

        ProjectsSample again = ProjectsSampleWorld.Create(_scratch.Path);

        Assert.Equal(first, again.PathOf("deckle"));
        Assert.False(File.Exists(Path.Combine(first, "left-over.txt")));
    }
}
