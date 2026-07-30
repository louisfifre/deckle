using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    // ── Exclusion precedence ────────────────────────────────────────────────
    //
    // exclusions > packs > base lexicon. What the precedence buys is that the
    // user never has to know which source carried the word — so each source is
    // asserted separately, plus the case where both carry it.

    [Fact]
    public void Compose_ExcludesAWordTheBaseLexiconCarries()
    {
        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [], ["connexion"]);

        Assert.False(effective.Contains("connexion"));
        Assert.Equal(0.0, effective.FrequencyOf("connexion"));
    }

    [Fact]
    public void Compose_ExcludesAWordOnlyAPackCarries()
    {
        var effective = EffectiveLexicon.Compose(Lexicon(BaseTsv), [Lexicon(PackTsv)], ["qubit"]);

        Assert.False(effective.Contains("qubit"));
    }

    // The case precedence exists for: the pack raised the form, and the
    // exclusion must still win over the merged result, not over one source.
    [Fact]
    public void Compose_ExcludesAWordBothSourcesCarry()
    {
        var effective = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [Lexicon(PackTsv)], ["hebergeur"]);

        Assert.False(effective.Contains("hebergeur"));
    }

    [Fact]
    public void Compose_LeavesEveryOtherFormAloneWhenExcluding()
    {
        var effective = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [Lexicon(PackTsv)], ["qubit"]);

        Assert.Equal(100, effective.FrequencyOf("chat"));
        Assert.Equal(0.87, effective.FrequencyOf("hebergeur"));
        Assert.True(effective.Contains("plugin"));
    }

    // The register is the user's and may name anything; a word no lexicon
    // carries is nothing to remove, never an error.
    [Fact]
    public void Compose_IgnoresAnExclusionNoSourceCarries()
    {
        var effective = EffectiveLexicon.Compose(
            Lexicon(BaseTsv), [], ["motquinexistepas"]);

        Assert.Equal(3, effective.Count);
    }

    // ── The exclusion register ──────────────────────────────────────────────

    [Theory]
    [InlineData("Connexion", "connexion")]   // case folds — the lexicon is lowercased
    [InlineData("  qubit  ", "qubit")]       // stray whitespace is the user's, not the word's
    public void NormalizeExcludedWord_SpellsAWordTheWayTheLexiconDoes(
        string typed, string expected)
    {
        Assert.Equal(expected, AutocorrectSettings.NormalizeExcludedWord(typed));
    }

    // An exclusion removes ONE lexicon key, so anything that cannot name one is
    // refused rather than stored as an entry that could never match.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deux mots")]
    [InlineData(null)]
    public void NormalizeExcludedWord_RefusesWhatCannotNameASingleForm(string? typed)
    {
        Assert.Null(AutocorrectSettings.NormalizeExcludedWord(typed));
    }

    [Fact]
    public void WithExclusion_KeepsTheRegisterSortedAndFreeOfDuplicates()
    {
        List<string> register = AutocorrectSettings.WithExclusion([], "qubit");
        register = AutocorrectSettings.WithExclusion(register, "chat");
        register = AutocorrectSettings.WithExclusion(register, "qubit");

        Assert.Equal(["chat", "qubit"], register);
    }

    [Fact]
    public void WithoutExclusion_PutsTheWordBack()
    {
        List<string> register = AutocorrectSettings.WithExclusion(["chat", "qubit"], "plugin");

        Assert.Equal(["chat", "plugin", "qubit"], register);
        Assert.Equal(["chat", "qubit"], AutocorrectSettings.WithoutExclusion(register, "plugin"));
    }

    // The settings file is editable by hand, and an entry spelled unlike the
    // lexicon would silently exclude nothing.
    [Fact]
    public void OnDeserialized_NormalizesAndDeduplicatesTheRegister()
    {
        var settings = new AutocorrectSettings
        {
            ExcludedWords = ["Qubit", "qubit", "  Chat ", "", "deux mots"],
        };

        settings.OnDeserialized();

        Assert.Equal(["qubit", "chat"], settings.ExcludedWords);
    }

    // The language set is an argument to the key, so these fix it explicitly
    // rather than reading the machine's — the assertions are about the key's
    // algebra, not about what Windows happens to be configured with.
    private static readonly IReadOnlySet<string> NoLanguages =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EffectiveLexiconKey_ChangesWithTheExclusionRegister()
    {
        var none = new AutocorrectSettings();
        var one = new AutocorrectSettings { ExcludedWords = ["qubit"] };

        Assert.NotEqual(
            DomainActivation.EffectiveLexiconKey(none, NoLanguages),
            DomainActivation.EffectiveLexiconKey(one, NoLanguages));
    }

    [Fact]
    public void EffectiveLexiconKey_IgnoresExclusionOrder()
    {
        var forward = new AutocorrectSettings { ExcludedWords = ["qubit", "chat"] };
        var reversed = new AutocorrectSettings { ExcludedWords = ["chat", "qubit"] };

        Assert.Equal(
            DomainActivation.EffectiveLexiconKey(forward, NoLanguages),
            DomainActivation.EffectiveLexiconKey(reversed, NoLanguages));
    }

    // ── Activation state ────────────────────────────────────────────────────

    [Fact]
    public void DomainPacks_FollowTheSystemLanguagesUntilTheUserDecides()
    {
        var settings = new AutocorrectSettings();

        Assert.Empty(DomainActivation.ActiveIn(settings, NoLanguages));
        Assert.Equal(
            DomainPack.Shipped,
            DomainActivation.ActiveIn(
                settings,
                LanguagesOf(DomainPack.Shipped.Select(pack => pack.Language).ToArray())));
    }

    [Fact]
    public void DomainPacks_ActiveInReturnsTheShippedPackOnceActivated()
    {
        DomainPack shipped = DomainPack.Shipped[0];
        var settings = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { [shipped.Id] = true },
        };

        Assert.Equal([shipped], DomainActivation.ActiveIn(settings, NoLanguages));
    }

    // The key is an identity, not a history: the App rebuilds the engine when
    // it changes, so two settings that describe the same table must agree, and
    // a deactivated pack must not linger in it. An id no build ships names no
    // forms, so it cannot move the key either.
    [Fact]
    public void EffectiveLexiconKey_IgnoresIdsNoBuildShips()
    {
        var bare = new AutocorrectSettings();
        var fanciful = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { ["fr-med"] = true },
        };

        Assert.Equal(
            DomainActivation.EffectiveLexiconKey(bare, NoLanguages),
            DomainActivation.EffectiveLexiconKey(fanciful, NoLanguages));
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
            DomainActivation.EffectiveLexiconKey(active, NoLanguages),
            DomainActivation.EffectiveLexiconKey(inactive, NoLanguages));
    }

    private static IReadOnlySet<string> LanguagesOf(params string[] languages) =>
        new HashSet<string>(languages, System.StringComparer.OrdinalIgnoreCase);
}
