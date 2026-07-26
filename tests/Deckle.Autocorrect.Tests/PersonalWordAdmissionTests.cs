using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class PersonalWordAdmissionTests
{
    private static PersonalWordAdmission Gate()
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader(
            "prépare\t100\npréparé\t80\nsur\t500\nsûr\t250\ntélémétrie\t0.85\nmodèle\t36\n"));
        return new PersonalWordAdmission(french, AccentIndex.Build(french));
    }

    [Fact]
    public void RejectsUnknownBareFormOfAnAccentedFrenchWord()
    {
        Assert.False(Gate().Allows("prepare"));
    }

    [Fact]
    public void AllowsExactFrenchLiteralDespiteAnAccentedSibling()
    {
        Assert.True(Gate().Allows("sur"));
    }

    [Fact]
    public void AllowsUnknownWordWithoutAccentCollision()
    {
        Assert.True(Gate().Allows("anytype"));
    }

    [Fact]
    public void RareFrenchCollisionDoesNotPermanentlyBanARecurringLiteral()
    {
        Assert.True(Gate().Allows("telemetry"));
    }

    [Fact]
    public void ExplicitTechnicalLiteralOutranksAFrequentFrenchCollision()
    {
        var french = FrequencyLexicon.LoadTsv(new StringReader("modèle\t36\n"));
        var protectedLiterals = new GlobalEnglishLexicon(generatedSeed: null);
        var gate = new PersonalWordAdmission(
            french, AccentIndex.Build(french), protectedLiterals);

        Assert.True(gate.Allows("model"));
    }
}
