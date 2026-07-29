using System.IO;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class GenderVariantProbeTests
{
    [Theory]
    [InlineData("un", "une")]
    [InlineData("une", "un")]
    [InlineData("seul", "seule")]
    [InlineData("seule", "seul")]
    public void ExposesOneLexiconBackedTerminalGenderAlternative(
        string literal,
        string alternative)
    {
        GenderVariantProbe probe = Probe(
            "un\t100\nune\t90\nseul\t80\nseule\t70\n");

        IReadOnlyList<AccentVariant> candidates =
            probe.AmbiguousCandidates(literal);

        Assert.Equal(
            new[] { literal, alternative }.Order(),
            candidates.Select(candidate => candidate.Form).Order());
    }

    [Fact]
    public void RefusesAnInventedOrUnprotectedEndpoint()
    {
        GenderVariantProbe probe = Probe("erreur\t100\navec\t90\n");

        Assert.Empty(probe.AmbiguousCandidates("erreur"));
        Assert.Empty(probe.AmbiguousCandidates("avec"));
    }

    private static GenderVariantProbe Probe(string tsv) =>
        new(FrequencyLexicon.LoadTsv(new StringReader(tsv)));
}
