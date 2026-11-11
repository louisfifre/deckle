using Xunit;

namespace Deckle.Atelier.Tests;

// The map of a folder: what it finds, what an agent always reads, the links
// both ways, and how it behaves on an empty, missing or huge folder.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class MapInventoryTests
{
    private static MapInventory Sample(ScratchFolder scratch)
    {
        MapSampleRepository.Write(scratch);
        return Map(scratch.Path);
    }

    private static MapInventory Map(string folder, MapLimits? limits = null) =>
        MapInventory.Build(folder, TestContext.Current.CancellationToken, limits);

    private static MapItem Item(MapInventory map, string id) =>
        map.Find(id) ?? throw new Xunit.Sdk.XunitException($"no item {id}; found {string.Join(", ", map.Items.Select(i => i.Id))}");

    [Fact]
    public void EachThingIsFoundWithItsKind()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        Assert.True(map.Succeeded);
        Assert.False(map.IsEmpty);
        var kinds = map.Items.ToDictionary(i => i.Id, i => i.Kind);
        Assert.Equal(MapItemKind.ContextFile, kinds["CLAUDE.md"]);
        Assert.Equal(MapItemKind.ContextFile, kinds["AGENTS.md"]);
        Assert.Equal(MapItemKind.ContextFile, kinds["src/CLAUDE.md"]);
        Assert.Equal(MapItemKind.ContextFile, kinds[".claude/rules/style.md"]);
        Assert.Equal(MapItemKind.Skill, kinds[".claude/skills/publier-version/SKILL.md"]);
        Assert.Equal(MapItemKind.Skill, kinds[".claude/skills/capture-ecran/SKILL.md"]);
        Assert.Equal(MapItemKind.McpServer, kinds[".mcp.json#navigateur"]);
        Assert.Equal(MapItemKind.McpServer, kinds[".mcp.json#notes-locales"]);
        Assert.Equal(MapItemKind.Script, kinds["scripts/build.ps1"]);
        Assert.Equal(MapItemKind.Script, kinds["tools/check-links.py"]);
        Assert.Equal(MapItemKind.Script, kinds["tools/run.js"]);
        Assert.Equal(MapItemKind.StandardFile, kinds["README.md"]);
        Assert.Equal(MapItemKind.StandardFile, kinds["CONTRIBUTING.md"]);
        Assert.Equal(MapItemKind.StandardFile, kinds["LICENSE"]);

        Assert.DoesNotContain("lib/helper.py", kinds.Keys);
        Assert.DoesNotContain("docs/guide.md", kinds.Keys);
        Assert.DoesNotContain("src/CONTRIBUTING.md", kinds.Keys);
        Assert.DoesNotContain(".claude/skills/publier-version/references/steps.md", kinds.Keys);
    }

    [Fact]
    public void ItemsComeInSectionOrderThenByName()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        Assert.Equal(map.Items.Select(i => i.Kind).Order(), map.Items.Select(i => i.Kind));
        Assert.Equal(["capture-ecran", "publier-version"], map.Items.Where(i => i.Kind == MapItemKind.Skill).Select(i => i.Name));
    }

    [Fact]
    public void AnItemCarriesItsLinesAndAnEstimateOfItsTokens()
    {
        using var scratch = new ScratchFolder();
        scratch.File(new string('a', 10) + "\n" + new string('b', 9), "README.md");

        MapItem readme = Item(Map(scratch.Path), "README.md");

        Assert.Equal(2, readme.Lines);
        Assert.Equal(5, readme.EstimatedTokens);
    }

    [Fact]
    public void AServerSaysWhereItIsDeclaredAndHowItStartsWithoutItsSecrets()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapItem navigateur = Item(map, ".mcp.json#navigateur");
        MapItem notes = Item(map, ".mcp.json#notes-locales");

        Assert.Equal(".mcp.json", navigateur.RelativePath);
        Assert.Equal(3, navigateur.DeclaredAtLine);
        Assert.Equal(4, notes.DeclaredAtLine);
        Assert.Equal("npx", navigateur.Summary);
        Assert.Equal("http://localhost:3000/mcp", notes.Summary);
        Assert.Null(navigateur.Lines);
        Assert.Null(navigateur.EstimatedTokens);
    }

    [Fact]
    public void TheVsCodeFileDeclaresItsServersUnderServersAndMayHoldComments()
    {
        using var scratch = new ScratchFolder();
        scratch.File("""
            // servers for this workspace
            { "servers": { "fetch": { "command": "uvx", }, }, }
            """, ".vscode", "mcp.json");

        MapInventory map = Map(scratch.Path);

        Assert.Equal(["fetch"], map.Items.Select(i => i.Name));
    }

    [Fact]
    public void AConfigurationThatIsNotJsonIsSkippedAndSaidSo()
    {
        using var scratch = new ScratchFolder();
        scratch.File("{ not json", ".mcp.json");

        MapInventory map = Map(scratch.Path);

        Assert.True(map.IsEmpty);
        Assert.Contains(new MapSkip(".mcp.json", MapSkipReason.Unreadable), map.Skipped);
    }

    [Fact]
    public void TheRootClaudeMdAndWhatItImportsAreAlwaysRead()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapItem claude = Item(map, "CLAUDE.md");
        MapItem agents = Item(map, "AGENTS.md");

        Assert.True(claude.IsAlwaysRead);
        Assert.Equal(MapReaders.ClaudeCode, claude.ReadAtStartBy);
        Assert.True(agents.IsAlwaysRead);
        Assert.True(agents.ReadAtStartBy.HasFlag(MapReaders.ClaudeCode));
        Assert.True(agents.ReadAtStartBy.HasFlag(MapReaders.Codex));
        // The import in backticks stays literal: README is not loaded.
        Assert.Equal(MapLoading.NotLoaded, Item(map, "README.md").Loading);
    }

    [Fact]
    public void WhatLoadsLaterIsNotAlwaysRead()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        Assert.Equal(MapLoading.OnDemand, Item(map, "src/CLAUDE.md").Loading);
        Assert.Equal(MapLoading.AtSessionStart, Item(map, ".claude/rules/style.md").Loading);
        Assert.Equal(MapLoading.OnDemand, Item(map, ".claude/rules/api.md").Loading);
        Assert.Equal(MapLoading.NotLoaded, Item(map, "scripts/build.ps1").Loading);

        MapItem skill = Item(map, ".claude/skills/publier-version/SKILL.md");
        Assert.Equal(MapLoading.OnDemand, skill.Loading);
        Assert.Equal("Monte la version et publie une release.", skill.Summary);
        Assert.Equal(TokenEstimate.Of("publier-version: Monte la version et publie une release."), skill.EstimatedStartupTokens);
        Assert.True(skill.EstimatedStartupTokens < skill.EstimatedTokens);
    }

    [Fact]
    public void TheAlwaysReadTotalSumsTheAlwaysReadItems()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        int expected = new[] { "CLAUDE.md", "AGENTS.md", ".claude/rules/style.md" }.Sum(id => Item(map, id).EstimatedTokens!.Value);
        Assert.Equal(expected, map.EstimatedAlwaysReadTokens);
        Assert.Equal(3, map.Items.Count(i => i.IsAlwaysRead));
    }

    [Fact]
    public void ClaudeCodeReadsAgentsMdOnlyWithoutAClaudeMd()
    {
        using var alone = new ScratchFolder();
        alone.File("Rules.\n", "AGENTS.md");
        using var beside = new ScratchFolder();
        beside.File("Rules.\n", "AGENTS.md");
        beside.File("Mine.\n", "CLAUDE.md");

        Assert.True(Item(Map(alone.Path), "AGENTS.md").ReadAtStartBy.HasFlag(MapReaders.ClaudeCode));
        MapItem agents = Item(Map(beside.Path), "AGENTS.md");
        Assert.True(agents.IsAlwaysRead);
        Assert.False(agents.ReadAtStartBy.HasFlag(MapReaders.ClaudeCode));
    }

    [Fact]
    public void AnImportedFileBecomesAContextFileUpToFourHops()
    {
        using var scratch = new ScratchFolder();
        scratch.File("@docs/a.md\n", "CLAUDE.md");
        scratch.File("@b.md\n", "docs", "a.md");
        scratch.File("@c.md\n", "docs", "b.md");
        scratch.File("@d.md\n", "docs", "c.md");
        scratch.File("@e.md\n", "docs", "d.md");
        scratch.File("Too deep.\n", "docs", "e.md");

        MapInventory map = Map(scratch.Path);

        foreach (string id in (string[])["docs/a.md", "docs/b.md", "docs/c.md", "docs/d.md"])
        {
            Assert.Equal(MapItemKind.ContextFile, Item(map, id).Kind);
            Assert.True(Item(map, id).IsAlwaysRead, id);
        }
        Assert.Null(map.Find("docs/e.md"));
        MapLink import = Assert.Single(map.LinksFrom(Item(map, "CLAUDE.md")));
        Assert.Equal(new MapLinkEvidence(MapLinkForm.Import, "CLAUDE.md", 1), Assert.Single(import.Evidence));
    }

    [Fact]
    public void LinksAreKnownBothWaysWithWhereTheyWereFound()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);
        MapItem skill = Item(map, ".claude/skills/publier-version/SKILL.md");
        MapItem claude = Item(map, "CLAUDE.md");

        Assert.Equal(
            [".mcp.json#notes-locales", "CLAUDE.md", "scripts/build.ps1", "scripts/capture.ps1", "scripts/release.ps1"],
            map.LinksFrom(skill).Select(l => l.ToId).Order(StringComparer.Ordinal));
        Assert.Equal(["CLAUDE.md"], map.LinksTo(skill).Select(l => l.FromId));

        MapLink back = map.LinksFrom(skill).Single(l => l.ToId == "CLAUDE.md");
        Assert.True(map.IsMutual(back));
        Assert.Equal(new MapLinkEvidence(MapLinkForm.MarkdownLink, ".claude/skills/publier-version/SKILL.md", 7), Assert.Single(back.Evidence));

        MapLink call = map.LinksFrom(claude).Single(l => l.ToId == skill.Id);
        Assert.Equal(MapLinkForm.Mention, Assert.Single(call.Evidence).Form);
        Assert.Equal(5, map.Neighbours(skill).Count);
    }

    [Fact]
    public void ASkillsSupportingFileSpeaksForTheSkill()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        MapLink capture = map.LinksFrom(Item(map, ".claude/skills/publier-version/SKILL.md")).Single(l => l.ToId == "scripts/capture.ps1");

        Assert.Equal(".claude/skills/publier-version/references/steps.md", Assert.Single(capture.Evidence).RelativePath);
    }

    [Fact]
    public void AScriptThatCallsAnotherLinksToIt()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        Assert.Equal(["scripts/build.ps1"], map.LinksFrom(Item(map, "scripts/release.ps1")).Select(l => l.ToId));
        Assert.Equal(
            [".claude/skills/publier-version/SKILL.md", "AGENTS.md", "README.md", "scripts/release.ps1"],
            map.LinksTo(Item(map, "scripts/build.ps1")).Select(l => l.FromId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AScriptNameTwoScriptsShareLinksOnlyWithItsPath()
    {
        using var scratch = new ScratchFolder();
        scratch.File("x\n", "a", "run.ps1");
        scratch.File("x\n", "b", "run.ps1");
        scratch.File("Use run.ps1, or b/run.ps1.\n", "README.md");

        MapInventory map = Map(scratch.Path);

        Assert.Equal(["b/run.ps1"], map.LinksFrom(Item(map, "README.md")).Select(l => l.ToId));
    }

    [Fact]
    public void AnItemsPathWrittenInCodeLinksToIt()
    {
        using var scratch = new ScratchFolder();
        scratch.File("x\n", "scripts", "README.md");
        scratch.File("What is installed: `scripts/README.md`.\n", "AGENTS.md");

        MapInventory map = Map(scratch.Path);

        Assert.Equal(["scripts/README.md"], map.LinksFrom(Item(map, "AGENTS.md")).Select(l => l.ToId));
    }

    [Fact]
    public void WebLinksAnchorsAndCodeBlocksLinkNothing()
    {
        using var scratch = new ScratchFolder();
        scratch.File("x\n", "CONTRIBUTING.md");
        scratch.File("""
            [site](https://example.com/CONTRIBUTING.md) [top](#top)
            ```
            [inside](CONTRIBUTING.md)
            ```
            """, "README.md");

        MapInventory map = Map(scratch.Path);

        Assert.Empty(map.Links);
    }

    [Fact]
    public void AFolderWithNothingOfTheKindIsEmptyAndSaysWhatWasLookedFor()
    {
        using var scratch = new ScratchFolder();
        scratch.File("total 12", "facture-2026-01.txt");
        scratch.File("x", "notes.md");

        MapInventory map = Map(scratch.Path);

        Assert.True(map.Succeeded);
        Assert.True(map.IsEmpty);
        Assert.Empty(map.Links);
        Assert.Equal(Enum.GetValues<MapItemKind>(), MapInventory.KindsLookedFor);
    }

    [Fact]
    public void AFolderThatIsGoneSaysSo()
    {
        using var scratch = new ScratchFolder();

        MapInventory map = Map(Path.Combine(scratch.Path, "gone"));

        Assert.Equal(MapInventoryFailure.NotFound, map.Failure);
        Assert.False(map.IsEmpty);
    }

    [Fact]
    public void HistoryPackagesAndBuildOutputsAreSkippedWithTheirReason()
    {
        using var scratch = new ScratchFolder();
        MapInventory map = Sample(scratch);

        Assert.Contains(new MapSkip(".git", MapSkipReason.VersionControl), map.Skipped);
        Assert.Contains(new MapSkip("node_modules", MapSkipReason.Dependencies), map.Skipped);
        Assert.Contains(new MapSkip("bin", MapSkipReason.BuildOutput), map.Skipped);
        Assert.Contains(new MapSkip("obj", MapSkipReason.BuildOutput), map.Skipped);
        Assert.DoesNotContain(map.Items, i => i.RelativePath.StartsWith("bin/") || i.RelativePath.StartsWith("node_modules/"));
    }

    [Fact]
    public void AHugeFolderStopsAtTheLimitAndSaysTheMapIsPartial()
    {
        using var scratch = new ScratchFolder();
        for (int i = 0; i < 30; i++) scratch.File("x", $"s{i:00}.ps1");

        MapInventory map = Map(scratch.Path, limits: new MapLimits(MaxEntries: 10));

        Assert.True(map.IsTruncated);
        Assert.Equal(10, map.Items.Count);
        Assert.Contains(map.Skipped, s => s.Reason == MapSkipReason.EntryLimit);
    }

    [Fact]
    public void AFileOverTheSizeLimitIsListedButNotCounted()
    {
        using var scratch = new ScratchFolder();
        scratch.File(new string('x', 200), "README.md");

        MapInventory map = Map(scratch.Path, limits: new MapLimits(MaxFileBytes: 100));

        MapItem readme = Item(map, "README.md");
        Assert.Null(readme.Lines);
        Assert.Null(readme.EstimatedTokens);
        Assert.Contains(new MapSkip("README.md", MapSkipReason.TooLarge), map.Skipped);
    }

    [Fact]
    public void ACancelledMapStops()
    {
        using var scratch = new ScratchFolder();
        MapSampleRepository.Write(scratch);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => MapInventory.Build(scratch.Path, cancellation.Token));
    }
}
