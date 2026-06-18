using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Case transfer: the candidate is stored lowercase, but the correction must
// wear the case the user typed. Three shapes recognised, the rest pass through.
[Trait("Category", "unit")]
public class CasePatternTests
{
    [Fact]
    public void AllLowerKeepsReplacementAsIs()
    {
        Assert.Equal("français", CasePattern.Apply("francais", "français"));
    }

    [Fact]
    public void FirstUpperCapitalisesReplacement()
    {
        // The accented head must survive the uppercasing: É, not E.
        Assert.Equal("École", CasePattern.Apply("Ecole", "école"));
    }

    [Fact]
    public void SingleUpperCharIsTreatedAsFirstUpper()
    {
        Assert.Equal("À", CasePattern.Apply("A", "à"));
    }

    [Fact]
    public void AllUpperUppercasesReplacement()
    {
        Assert.Equal("FRANÇAIS", CasePattern.Apply("FRANCAIS", "français"));
    }

    [Fact]
    public void MixedCasePassesThroughAsIs()
    {
        // An irregular shape is left to the lowercase form — conservative.
        Assert.Equal("français", CasePattern.Apply("fRaNcAis", "français"));
    }

    [Fact]
    public void EmptyInputsReturnReplacement()
    {
        Assert.Equal("école", CasePattern.Apply("", "école"));
    }
}
