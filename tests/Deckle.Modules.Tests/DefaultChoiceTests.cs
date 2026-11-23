using System.Collections.Generic;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// What the selector pre-checks for a new person: the modules flagged
// DefaultSelected plus what they need. Every other module stays selectable.
[Trait("Category", "unit")]
public class DefaultChoiceTests
{
    private static ModuleDescriptor Module(string id, bool isDefault, params string[] deps) => new()
    {
        Id = id,
        Glyph = "",
        DefaultSelected = isDefault,
        DependsOn = deps,
    };

    [Fact]
    public void OnlyFlaggedModulesAreSeeded()
    {
        IReadOnlyList<ModuleDescriptor> catalogue =
        [
            Module("transcription", true),
            Module("ambient", false),
            Module("trackpad", false),
        ];

        Assert.Equal(new HashSet<string> { "transcription" }, ModulePresence.DefaultChoiceFor(catalogue));
    }

    [Fact]
    public void ASeededModulePullsItsDependenciesEvenWhenUnflagged()
    {
        IReadOnlyList<ModuleDescriptor> catalogue =
        [
            Module("transcription", false),
            Module("rewrite", true, "transcription"),
        ];

        Assert.Equal(
            new HashSet<string> { "transcription", "rewrite" },
            ModulePresence.DefaultChoiceFor(catalogue));
    }

    [Fact]
    public void ADescriptorIsNotSeededByDefault()
    {
        Assert.False(Module("x", false).DefaultSelected);
        Assert.False(new ModuleDescriptor { Id = "x", Glyph = "" }.DefaultSelected);
    }

    [Fact]
    public void NoFlaggedModuleSeedsNothing()
    {
        IReadOnlyList<ModuleDescriptor> catalogue = [Module("a", false), Module("b", false)];

        Assert.Empty(ModulePresence.DefaultChoiceFor(catalogue));
    }
}
