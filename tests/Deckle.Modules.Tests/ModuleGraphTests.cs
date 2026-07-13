using System.Collections.Generic;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Tests de comportement sur la cascade du sélecteur — le contrat entre le
// catalogue (arêtes DependsOn) et la page à cocher. On vérifie ce que la
// cascade PRODUIT (l'ensemble coché résultant), pas comment elle marche : le
// graphe est un module pur, exerçable tel quel.
[Trait("Category", "unit")]
public class ModuleGraphTests
{
    private static ModuleDescriptor Module(string id, params string[] deps) => new()
    {
        Id = id,
        Glyph = "",
        DependsOn = deps,
    };

    // rewrite → transcription : la forme réelle du catalogue aujourd'hui.
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

        // b n'est pas coché ; décocher a doit quand même expulser c à travers lui.
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
        Assert.Contains("ambient", check);

        var uncheck = ModuleGraph.WithoutDependents(Catalog, ["ambient", "rewrite"], "rewrite");
        Assert.Contains("ambient", uncheck);
    }
}
