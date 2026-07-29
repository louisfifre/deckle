using System;
using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// A language row's default follows the Windows language list, and the toggle
// the user flips is an override that detection never takes back. That rule is
// the whole product here — the settings file records decisions, not detections,
// so a language added in Windows later still reaches the packs nobody touched.
[Trait("Category", "unit")]
public class DomainActivationTests
{
    private static readonly DomainPack Pack = DomainPack.Shipped[0];

    private static IReadOnlySet<string> Languages(params string[] languages) =>
        new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void UndecidedPack_IsActiveWhenItsLanguageIsASystemLanguage()
    {
        var settings = new AutocorrectSettings();

        Assert.True(DomainActivation.IsActive(settings, Pack, Languages(Pack.Language)));
    }

    [Fact]
    public void UndecidedPack_IsInactiveOutsideTheSystemLanguages()
    {
        var settings = new AutocorrectSettings();

        Assert.False(DomainActivation.IsActive(settings, Pack, Languages("sv")));
    }

    // The decision outranks the detection in both directions: a pack turned off
    // in a language the user writes stays off…
    [Fact]
    public void DecliningAPack_SurvivesItsLanguageBeingASystemLanguage()
    {
        var settings = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { [Pack.Id] = false },
        };

        Assert.False(DomainActivation.IsActive(settings, Pack, Languages(Pack.Language)));
    }

    // …and a pack turned on in a language Windows never heard of stays on.
    [Fact]
    public void ActivatingAPack_SurvivesItsLanguageBeingUnknownToWindows()
    {
        var settings = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { [Pack.Id] = true },
        };

        Assert.True(DomainActivation.IsActive(settings, Pack, Languages("sv")));
    }

    // Two states, one table: refusing a pack and never meeting it outside your
    // languages describe the same lexicon, so the engine must not rebuild when
    // one becomes the other.
    [Fact]
    public void EffectiveLexiconKey_MatchesForADeclinedAndAnUndecidedPack()
    {
        var declined = new AutocorrectSettings
        {
            DomainPacks = new Dictionary<string, bool> { [Pack.Id] = false },
        };
        var undecided = new AutocorrectSettings();

        Assert.Equal(
            DomainActivation.EffectiveLexiconKey(declined, Languages(Pack.Language)),
            DomainActivation.EffectiveLexiconKey(undecided, Languages("sv")));
    }

    [Fact]
    public void EffectiveLexiconKey_IgnoresTheOrderOfTheSystemLanguages()
    {
        var settings = new AutocorrectSettings();

        Assert.Equal(
            DomainActivation.EffectiveLexiconKey(settings, Languages(Pack.Language, "sv")),
            DomainActivation.EffectiveLexiconKey(settings, Languages("sv", Pack.Language)));
    }
}
