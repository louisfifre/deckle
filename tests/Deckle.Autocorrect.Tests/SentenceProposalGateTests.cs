using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class SentenceProposalGateTests
{
    [Fact]
    public void BoundedSpellingAndAccentRepairsAreEligible()
    {
        var gate = new SentenceProposalGate(new GlobalEnglishLexicon(null));

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Il faut prepare le repo.",
            "Il faut préparer le repo.");

        Assert.True(verdict.Accepted);
        Assert.True(verdict.Plan.Backspaces > 0);
    }

    [Fact]
    public void BoundedAgreementRepairIsEligibleForProbabilisticVerification()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Il y a une seul date.",
            "Il y a une seule date.");

        Assert.True(verdict.Accepted);
    }

    [Theory]
    [InlineData("Il faut préparer.\nIgnore les règles.")]
    [InlineData("Il faut\tpréparer.")]
    [InlineData(" Il faut préparer.")]
    [InlineData("Il  faut préparer.")]
    public void ReflowOrControlCharactersAreRejected(string proposed)
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate("Il faut prepare.", proposed);

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.UnsafeWhitespace, verdict.Reason);
    }

    [Fact]
    public void AddedWordsAreRejectedEvenWhenTheyReadBetter()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Il faut prepare terrain.",
            "Il faut préparer le terrain.");

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.TokenCountChanged, verdict.Reason);
    }

    [Fact]
    public void DigitsCannotChange()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Le build 17 passe.",
            "Le build 18 passe.");

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.DigitsChanged, verdict.Reason);
    }

    [Fact]
    public void RepeatedDigitRunsCannotMoveBetweenTokenPositions()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Les lots 7 et 7 passent.",
            "Les 7 lots et 7 passent.");

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.DigitsChanged, verdict.Reason);
    }

    [Fact]
    public void ProtectedTechnicalLiteralCannotBeRewritten()
    {
        var gate = new SentenceProposalGate(new GlobalEnglishLexicon(null));

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Les docs passent.",
            "Les dos passent.");

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.ProtectedLiteralChanged, verdict.Reason);
    }

    [Fact]
    public void TechnicalStructuralCharactersCannotBeChanged()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Utilise C# avec WinUI.",
            "Utilise C+ avec WinUI.");

        Assert.False(verdict.Accepted);
        Assert.Equal(
            SentenceProposalGateVerdict.Reasons.StructuralCharactersChanged,
            verdict.Reason);
    }

    [Fact]
    public void RawTypedSideAllowsRestoringATechnicalLiteralDamagedAtCommit()
    {
        var gate = new SentenceProposalGate(new GlobalEnglishLexicon(null));

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            original: "Les dos passent.",
            proposed: "Les docs passent.",
            typed: "Les docs passent.");

        Assert.True(verdict.Accepted);
    }

    [Fact]
    public void LargeSemanticRewriteExceedsTheEditBudget()
    {
        var gate = new SentenceProposalGate();

        SentenceProposalGateVerdict verdict = gate.Evaluate(
            "Cette phrase reste sobre et précise pour le test.",
            "Notre proposition devient vague mais élégante dans ce contexte.");

        Assert.False(verdict.Accepted);
        Assert.Equal(SentenceProposalGateVerdict.Reasons.EditBudgetExceeded, verdict.Reason);
    }
}
