using System.Collections.Generic;
using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The effective lexicon: the single table the correctors consult once the base
// lexicon and the active domain packs have fused (CONTEXT.md § Lexicon
// composition). The product here is the merge law, not the plumbing — highest
// frequency wins, and the result cannot depend on which pack the user turned on
// first. Those two properties are what let the engine keep taking one lexicon
// and let activation order stay a non-question, so they are what is asserted.
[Trait("Category", "unit")]
public class EffectiveLexiconTests
{
    private const string BaseTsv =
        "chat\t100\nhebergeur\t0.5\nconnexion\t30\n";

    private const string PackTsv =
        "hebergeur\t0.87\nqubit\t0.2\nplugin\t0.2\n";

    private const string OtherPackTsv =
        "qubit\t5\nvecteur\t0.2\n";

    private static FrequencyLexicon Lexicon(string tsv) =>
        FrequencyLexicon.LoadTsv(new StringReader(tsv));

    // ── What the pack adds ──────────────────────────────────────────────────

    [Fact]
    public void Compose_AddsPackFormsAbsentFromTheBase()
    {
        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [Lexicon(PackTsv)]);

        Assert.True(effective.Contains("qubit"));
        Assert.Equal(0.2, effective.FrequencyOf("qubit"));
    }

    [Fact]
    public void Compose_KeepsBaseFormsThePackDoesNotCarry()
    {
        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [Lexicon(PackTsv)]);

        Assert.Equal(100, effective.FrequencyOf("chat"));
        Assert.Equal(30, effective.FrequencyOf("connexion"));
    }

    // ── The merge law ───────────────────────────────────────────────────────

    // A shared form keeps the HIGHEST frequency, never the sum the plain
    // FrequencyLexicon load applies to duplicate rows: a pack promotion
    // (« hebergeur » at 0.87 opm, the value the IT bench settled on) must lift
    // the form, and two sources must never inflate each other.
    [Fact]
    public void Compose_KeepsTheHighestFrequencyOnASharedForm()
    {
        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [Lexicon(PackTsv)]);

        Assert.Equal(0.87, effective.FrequencyOf("hebergeur"));
    }

    [Fact]
    public void Compose_NeverDemotesABaseFormBelowItsOwnFrequency()
    {
        var packUnderTheBase = Lexicon("chat\t0.2\n");

        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [packUnderTheBase]);

        Assert.Equal(100, effective.FrequencyOf("chat"));
    }

    // Commutativity is the reason activation order is not a setting: the user
    // may turn packs on in any order and must land on the same table.
    [Fact]
    public void Compose_IsIndependentOfPackOrder()
    {
        var forward = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [Lexicon(PackTsv), Lexicon(OtherPackTsv)]);
        var reversed = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [Lexicon(OtherPackTsv), Lexicon(PackTsv)]);

        Assert.Equal(forward.Count, reversed.Count);
        foreach (var (form, frequency) in forward.Entries)
            Assert.Equal(frequency, reversed.FrequencyOf(form));
    }

    // Idempotence: composing the same pack twice is composing it once. It is
    // what makes a rebuild after a settings change safe to run at any time.
    [Fact]
    public void Compose_IsIdempotentOverARepeatedPack()
    {
        var once = EffectiveLexicon.Compose(Lexicon(BaseTsv), [Lexicon(PackTsv)]);
        var twice = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [Lexicon(PackTsv), Lexicon(PackTsv)]);

        Assert.Equal(once.Count, twice.Count);
        foreach (var (form, frequency) in once.Entries)
            Assert.Equal(frequency, twice.FrequencyOf(form));
    }

    // ── No pack ─────────────────────────────────────────────────────────────

    // With every pack off the engine must read exactly the pre-pack table —
    // the guarantee that a user who never activates anything is untouched by
    // the whole mechanism.
    [Fact]
    public void Compose_WithNoPackReturnsTheBaseLexiconUnchanged()
    {
        var baseLexicon = Lexicon(BaseTsv);

        var effective = EffectiveLexicon.Compose(baseLexicon, []);

        Assert.Same(baseLexicon, effective);
    }

    // ── Activation state ────────────────────────────────────────────────────

    [Fact]
    public void DomainPacks_AreInactiveUntilTheUserActivatesThem()
    {
        var settings = new AutocorrectSettings();

        Assert.Empty(DomainPack.ActiveIn(settings));
    }

    [Fact]
    public void DomainPacks_ActiveInReturnsTheShippedPackOnceActivated()
    {
        DomainPack shipped = DomainPack.Shipped[0];
        var settings = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { [shipped.Id] = true },
        };

        Assert.Equal([shipped], DomainPack.ActiveIn(settings));
    }

    // The key is an identity, not a history: the App rebuilds the engine when
    // it changes, so two settings that describe the same table must agree, and
    // a deactivated pack must not linger in it.
    [Fact]
    public void EffectiveLexiconKey_IgnoresActivationOrder()
    {
        var forward = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { ["fr-it"] = true, ["fr-med"] = true },
        };
        var reversed = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { ["fr-med"] = true, ["fr-it"] = true },
        };

        Assert.Equal(
            AutocorrectSettings.EffectiveLexiconKey(forward),
            AutocorrectSettings.EffectiveLexiconKey(reversed));
    }

    [Fact]
    public void EffectiveLexiconKey_ChangesWhenAPackIsDeactivated()
    {
        var active = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { ["fr-it"] = true },
        };
        var inactive = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { ["fr-it"] = false },
        };

        Assert.NotEqual(
            AutocorrectSettings.EffectiveLexiconKey(active),
            AutocorrectSettings.EffectiveLexiconKey(inactive));
    }
}
