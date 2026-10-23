using System.Collections.Generic;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Behavior tests of the selector cascade, the contract between the catalogue
// (DependsOn edges) and the checklist page. They assert what the cascade
// PRODUCES (the resulting checked set), not how it works.
[Trait("Category", "unit")]
public class ModuleGraphTests
{
    private static ModuleDescriptor Module(string id, params string[] deps) => new()
    {
        Id = id,
        Glyph = "",
        DependsOn = deps,
    };

    // rewrite → transcription mirrors the real catalogue (AppModules).
    private static readonly IReadOnlyList<ModuleDescriptor> Catalog =
    [
        Module("transcription"),
        Module("rewrite", "transcription"),
        Module("ambient"),
    ];

    [Fact]
    public void CheckingAModulePullsItsDependencies()
    {
        var result = ModuleGraph.WithDependencies(Catalog, ["ambient"], "rewrite");

        Assert.Equal(new HashSet<string> { "ambient", "rewrite", "transcription" }, result);
    }

    [Fact]
    public void UncheckingADependencyExpelsItsDependents()
    {
        var result = ModuleGraph.WithoutDependents(
            Catalog, ["transcription", "rewrite", "ambient"], "transcription");

        Assert.Equal(new HashSet<string> { "ambient" }, result);
    }

    [Fact]
    public void TheCascadeIsTransitive()
    {
        IReadOnlyList<ModuleDescriptor> chain =
        [
            Module("a"),
            Module("b", "a"),
            Module("c", "b"),
        ];

        var check = ModuleGraph.WithDependencies(chain, [], "c");
        Assert.Equal(new HashSet<string> { "a", "b", "c" }, check);

        var uncheck = ModuleGraph.WithoutDependents(chain, ["a", "b", "c"], "a");
        Assert.Empty(uncheck);
    }

    [Fact]
    public void AnAlreadyUncheckedIntermediateStillRelaysTheCascade()
    {
        IReadOnlyList<ModuleDescriptor> chain =
        [
            Module("a"),
            Module("b", "a"),
            Module("c", "b"),
        ];

        // b is unchecked; unchecking a must still expel c through it.
        var result = ModuleGraph.WithoutDependents(chain, ["a", "c"], "a");

        Assert.Empty(result);
    }

    [Fact]
    public void AnUnknownDependencyDoesNotBlockTheCheck()
    {
        IReadOnlyList<ModuleDescriptor> catalog = [Module("x", "ghost")];

        var result = ModuleGraph.WithDependencies(catalog, [], "x");

        Assert.Equal(new HashSet<string> { "x" }, result);
    }

    [Fact]
    public void ACycleDoesNotHangEitherWalk()
    {
        IReadOnlyList<ModuleDescriptor> cyclic =
        [
            Module("a", "b"),
            Module("b", "a"),
        ];

        var check = ModuleGraph.WithDependencies(cyclic, [], "a");
        Assert.Equal(new HashSet<string> { "a", "b" }, check);

        var uncheck = ModuleGraph.WithoutDependents(cyclic, ["a", "b"], "a");
        Assert.Empty(uncheck);
    }

    [Fact]
    public void TheRestOfTheSelectionIsUntouched()
    {
        var check = ModuleGraph.WithDependencies(Catalog, ["ambient"], "transcription");
        Assert.Equal(new HashSet<string> { "ambient", "transcription" }, check);

        var uncheck = ModuleGraph.WithoutDependents(Catalog, ["ambient", "rewrite"], "rewrite");
        Assert.Equal(new HashSet<string> { "ambient" }, uncheck);
    }

    // Checking only ever adds modules the catalogue knows, while unchecking
    // removes whatever id it is given: a stale recorded id can always be cleared
    // but never created.
    [Fact]
    public void AnUnknownIdCannotBeCheckedButCanBeUnchecked()
    {
        var check = ModuleGraph.WithDependencies(Catalog, ["ambient"], "ghost");
        Assert.Equal(new HashSet<string> { "ambient" }, check);

        var uncheck = ModuleGraph.WithoutDependents(Catalog, ["ambient", "ghost"], "ghost");
        Assert.Equal(new HashSet<string> { "ambient" }, uncheck);
    }

    [Fact]
    public void ARecordedChoiceOverACycleKeepsBothOrNeither()
    {
        IReadOnlyList<ModuleDescriptor> cyclic =
        [
            Module("a", "b"),
            Module("b", "a"),
        ];

        Assert.Equal(
            new HashSet<string> { "a", "b" },
            ModuleGraph.WithoutUnmetDependencies(cyclic, ["a", "b"]));
        Assert.Empty(ModuleGraph.WithoutUnmetDependencies(cyclic, ["a"]));
    }

    [Fact]
    public void AModuleIsCheckableOnlyOnceItsDependenciesAreChecked()
    {
        Assert.False(ModuleGraph.DependenciesMet(Catalog, new HashSet<string> { "ambient" }, "rewrite"));
        Assert.True(ModuleGraph.DependenciesMet(Catalog, new HashSet<string> { "transcription" }, "rewrite"));
        Assert.True(ModuleGraph.DependenciesMet(Catalog, new HashSet<string>(), "ambient"));
    }

    [Fact]
    public void AnUnknownDependencyDoesNotGreyAModuleOut()
    {
        IReadOnlyList<ModuleDescriptor> catalog = [Module("x", "ghost")];

        Assert.True(ModuleGraph.DependenciesMet(catalog, new HashSet<string>(), "x"));
    }

    [Fact]
    public void ARecordedChoiceLosesTheModulesWhoseDependenciesItLacks()
    {
        IReadOnlyList<ModuleDescriptor> chain =
        [
            Module("a"),
            Module("b", "a"),
            Module("c", "b"),
            Module("d"),
        ];

        // a is missing, so b goes, and c goes through b.
        var result = ModuleGraph.WithoutUnmetDependencies(chain, ["b", "c", "d"]);

        Assert.Equal(new HashSet<string> { "d" }, result);
    }
}
