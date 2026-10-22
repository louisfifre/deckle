using System;
using System.Collections.Generic;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Behavior tests of how a recorded choice answers for a module: kept, declined
// or never offered. The never-offered case is what keeps a module added to the
// catalogue after the choice from vanishing at the next boot.
[Trait("Category", "unit")]
public class PresenceChoiceTests
{
    private static PresenceChoice Choice(string[] present, string[] known) =>
        new(new HashSet<string>(present, StringComparer.Ordinal),
            new HashSet<string>(known, StringComparer.Ordinal));

    [Fact]
    public void AKeptModuleIsIncluded()
    {
        Assert.True(Choice(["a"], ["a", "b"]).Includes("a"));
    }

    [Fact]
    public void ADeclinedModuleIsNotIncluded()
    {
        Assert.False(Choice(["a"], ["a", "b"]).Includes("b"));
    }

    [Fact]
    public void ANeverOfferedModuleIsIncluded()
    {
        Assert.True(Choice(["a"], ["a", "b"]).Includes("c"));
    }

    [Fact]
    public void ResolveAddsTheNeverOfferedModulesAndLeavesTheDeclinedOnesOut()
    {
        var resolved = Choice(["a"], ["a", "b"]).Resolve(["a", "b", "c"]);

        Assert.Equal(new HashSet<string> { "a", "c" }, resolved);
    }

    [Fact]
    public void ResolveKeepsAKeptIdTheCatalogueNoLongerHas()
    {
        var resolved = Choice(["a", "gone"], ["a", "gone"]).Resolve(["a"]);

        Assert.Equal(new HashSet<string> { "a", "gone" }, resolved);
    }

    [Fact]
    public void ResolveAgreesWithIncludesOverTheCatalogue()
    {
        var choice = Choice(["a"], ["a", "b"]);
        string[] catalogue = ["a", "b", "c"];

        var resolved = choice.Resolve(catalogue);

        foreach (string id in catalogue)
            Assert.Equal(choice.Includes(id), resolved.Contains(id));
    }
}
