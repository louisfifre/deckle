using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class MorphologyCorrectionTests
{
    [Fact]
    public void ElidedSubjectSelectsTheAgreeingKeyboardRepair()
    {
        ICorrectionPolicy policy = Policy(
            "réfléchisse\t2.28\n",
            "réfléchisse\tréfléchir\tsub:pre:1s;sub:pre:3s;\t1\n");

        CorrectionDecision? decision = policy.Evaluate("refelchisse", ["qu'on"]);

        Assert.Equal("réfléchisse", decision!.Replacement);
    }

    [Fact]
    public void AvoirSelectsThePastParticipleAccent()
    {
        ICorrectionPolicy policy = Policy(
            "termine\t20\nterminé\t100\n",
            "termine\tterminer\tind:pre:1s;ind:pre:3s;\t1\n"
            + "terminé\tterminer\tpar:pas\t1\n");

        CorrectionDecision? decision = policy.Evaluate("termine", ["j'ai"]);

        Assert.Equal("terminé", decision!.Replacement);
    }

    [Fact]
    public void EtreLetsTheCommonTypoWinOverARareParticiple()
    {
        ICorrectionPolicy policy = Policy(
            "bien\t1000\nbiné\t10\n",
            "biné\tbiner\tpar:pas\t1\n");

        CorrectionDecision? decision = policy.Evaluate("bine", ["c'est"]);

        Assert.Equal("bien", decision!.Replacement);
    }

    [Fact]
    public void DeterminerNeverOverturnsAValidContentWord()
    {
        ICorrectionPolicy policy = Policy(
            "date\t100\ndaté\t10\n",
            "daté\tdater\tpar:pas\t1\n");

        Assert.Null(policy.Evaluate("date", ["la"]));
    }

    [Fact]
    public void MissingRegularPluralIsSynthesizedOnlyFromANonVerb()
    {
        ICorrectionPolicy policy = Policy("hébergement\t100\n", "");

        CorrectionDecision? decision = policy.Evaluate("hebergements", ["les"]);

        Assert.Equal("hébergements", decision!.Replacement);
    }

    [Fact]
    public void PluralSynthesisNeverOverturnsAValidLiteral()
    {
        ICorrectionPolicy policy = Policy("cotes\t100\ncôte\t90\n", "");

        Assert.Null(policy.Evaluate("cotes", ["les"]));
    }

    private static ICorrectionPolicy Policy(string frenchTsv, string verbsTsv)
    {
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(frenchTsv));
        VerbMorphology verbs = VerbMorphology.LoadTsv(new StringReader(verbsTsv));
        return new ConservativeTypoCorrector(
            french, accentIndex: AccentIndex.Build(french), verbs: verbs);
    }
}
