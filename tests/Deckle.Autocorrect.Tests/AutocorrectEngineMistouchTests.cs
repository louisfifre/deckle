using System.Collections.Generic;
using System.Linq;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The mistouch families through the whole live chain — raw keys through the
// real decoder and tracker, out as injection bursts. These pin the wiring the
// unit layers cannot see: the tracker's separator run reaching the corrector,
// the repair only firing where correction is armed, and the span injection
// carrying the on-screen boundary.
[Trait("Category", "unit")]
public class AutocorrectEngineMistouchTests
{
    private static readonly IReadOnlyList<MistouchFamilyRecord> Families = new[]
    {
        new MistouchFamilyRecord("sub ;→'", MistouchFamilyKinds.BoundaryApostrophe),
        new MistouchFamilyRecord("dropped space after ,", MistouchFamilyKinds.BoundaryMissingSpace, ","),
    };

    private static AutocorrectEngineHarness NewHarness() =>
        new(french: new StubFrequencyLexicon(new()
            {
                ["il"] = 1000, ["fait"] = 1000, ["beau"] = 1000,
            }),
            mistouchFamilies: Families);

    [Fact]
    public void RepairsTheApostropheSlipOnTheScreen()
    {
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("qu;il ");

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("qu;il ", "qu'il "), call);
        var applied = Assert.Single(h.Applied);
        Assert.Equal(CorrectionReason.MistouchFamily, applied.Reason);
    }

    [Fact]
    public void RepairsTheGluedCommaOnTheScreen()
    {
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("fait,beau ");

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("fait,beau ", "fait, beau "), call);
    }

    [Fact]
    public void LeavesTheSpacedCommaAlone()
    {
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("fait, beau ");

        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void WithholdsTheRepairWhereCorrectionIsNotArmed()
    {
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable(process: "stranger");
        Assert.True(h.Start());

        h.Type("qu;il ");

        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void ABackspaceIntoTheRunDisarmsTheRepair()
    {
        // The screen really holds the glued comma, but the tracker can no longer
        // vouch for the run it saw, so the family abstains rather than inject
        // against a screen it cannot prove.
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("fait, ");
        h.Backspace();
        h.Type("beau ");

        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void ARetypedRunIsFaithfulAgainAndRepairs()
    {
        // The retype re-commits « qu » on a fresh « ; », so the run matches the
        // screen again and the family repairs the NEXT word; only the reopened
        // word itself is exempt.
        using var h = NewHarness();
        h.Prober.Surface = AutocorrectEngineHarness.Editable();
        Assert.True(h.Start());

        h.Type("qu;");
        h.Backspace();
        h.Type(";il ");

        var call = Assert.Single(h.Injector.Calls);
        Assert.Equal(("qu;il ", "qu'il "), call);
    }

    private sealed class StubFrequencyLexicon(Dictionary<string, double> entries) : IFrequencyLexicon
    {
        public bool Contains(string lowerForm) => entries.ContainsKey(lowerForm);
        public double FrequencyOf(string lowerForm) => entries.GetValueOrDefault(lowerForm);
    }
}
