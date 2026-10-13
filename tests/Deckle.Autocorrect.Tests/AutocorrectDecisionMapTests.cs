using System;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The per-app decision map is the activation grid the engine reads live on its
// input thread. Two invariants break easily: each transform returns a NEW map,
// so a concurrent reader only ever sees a complete reference, never a half-built
// one; and the map stays case-insensitive, because the engine matches process
// names regardless of case.
[Trait("Category", "unit")]
public class AutocorrectDecisionMapTests
{
    private static Dictionary<string, bool> Map(params (string process, bool on)[] entries)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (process, on) in entries)
            map[process] = on;
        return map;
    }

    [Fact]
    public void WithDecision_ajoute_une_app_inconnue()
    {
        var result = AutocorrectSettings.WithDecision(Map(), "claude", enabled: true);
        Assert.True(result["claude"]);
    }

    [Fact]
    public void WithDecision_ecrase_une_app_existante()
    {
        var result = AutocorrectSettings.WithDecision(Map(("claude", true)), "claude", enabled: false);
        Assert.False(result["claude"]);
    }

    [Fact]
    public void WithDecision_garde_la_carte_insensible_a_la_casse()
    {
        var result = AutocorrectSettings.WithDecision(Map(), "Claude", enabled: true);
        Assert.True(result["claude"]);
        Assert.True(result["CLAUDE"]);
    }

    [Fact]
    public void WithDecision_ne_mute_pas_l_entree()
    {
        var input = Map(("notepad", true));
        AutocorrectSettings.WithDecision(input, "claude", enabled: true);
        Assert.False(input.ContainsKey("claude"));
        Assert.Single(input);
    }

    [Fact]
    public void WithoutDecision_retire_une_app_decidee()
    {
        var result = AutocorrectSettings.WithoutDecision(Map(("claude", true), ("notepad", true)), "claude");
        Assert.False(result.ContainsKey("claude"));
        Assert.True(result["notepad"]);
    }

    [Fact]
    public void WithoutDecision_matche_sans_egard_a_la_casse()
    {
        var result = AutocorrectSettings.WithoutDecision(Map(("claude", true)), "CLAUDE");
        Assert.Empty(result);
    }

    [Fact]
    public void WithoutDecision_ne_mute_pas_l_entree()
    {
        var input = Map(("claude", true));
        AutocorrectSettings.WithoutDecision(input, "claude");
        Assert.True(input.ContainsKey("claude"));
    }

    [Fact]
    public void WithoutDecision_laisse_une_app_absente_intacte()
    {
        var result = AutocorrectSettings.WithoutDecision(Map(("notepad", true)), "claude");
        Assert.True(result["notepad"]);
        Assert.Single(result);
    }
}
