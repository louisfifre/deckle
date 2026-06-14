using System.IO;
using Deckle.Input.Autocorrect;
using Deckle.Input.Autocorrect.Lab;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// The measuring harness: it strips accents off accented reference text to
// synthesize a typist's input, runs a policy over it, and bins each token into
// the five classes. These tests pin the binning and the two mechanics the eval
// stands on — accent-stripped typing simulation and left-context chaining of
// the policy's OUTPUT within a sentence.
[Trait("Category", "unit")]
public class RestorationEvaluatorTests
{
    // A scripted policy keyed by the typed input, optionally gated on the prev
    // word — enough to drive one token into each outcome class deliberately.
    private sealed class ScriptedPolicy : ICorrectionPolicy
    {
        private readonly Dictionary<string, (string repl, string? requirePrev)> _rules;

        public ScriptedPolicy(Dictionary<string, (string, string?)> rules) => _rules = rules;

        public string? LastPrevForA { get; private set; }

        public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext)
        {
            string? previousWord = leftContext.Count > 0 ? leftContext[^1] : null;
            if (word == "a")
                LastPrevForA = previousWord; // capture the chained left context.

            if (!_rules.TryGetValue(word, out var rule))
                return null; // no rule → leave the literal (a miss on accented text).
            if (rule.requirePrev is not null && rule.requirePrev != previousWord)
                return null;
            return new CorrectionDecision(word, rule.repl, CorrectionReason.LexicalGate);
        }
    }

    // « le chat marché était à côté. » — one token in each class:
    //   le    bare,     forced wrong  → FalseCorrection (the killer)
    //   chat  bare,     left alone    → Untouched
    //   marché accented, fixed right  → Restored
    //   était accented, left bare     → Missed
    //   à     accented, fixed right   → Restored (prev-gated, proves chaining)
    //   côté  accented, wrong form    → WrongForm
    private static ScriptedPolicy Policy() => new(new Dictionary<string, (string, string?)>
    {
        ["le"] = ("là", null),          // mangles a correctly-typed word
        ["marche"] = ("marché", null),  // correct restoration
        // "etait" has no rule → Missed
        ["a"] = ("à", "etait"),         // only fires when prev is the prior output
        ["cote"] = ("coté", null),      // wrong accented form (ref is "côté")
    });

    private static RestorationReport Evaluate(string reference, ICorrectionPolicy policy) =>
        RestorationEvaluator.Evaluate(new StringReader(reference), policy);

    [Fact]
    public void BinsEachTokenIntoItsClass()
    {
        var report = Evaluate("le chat marché était à côté.", Policy());

        Assert.Equal(6L, report.TotalTokens);

        // Accented world: marché + à restored, était missed, côté wrong form.
        Assert.Equal(4L, report.AccentedRef);
        Assert.Equal(2L, report.Restored);
        Assert.Equal(1L, report.Missed);
        Assert.Equal(1L, report.WrongForm);

        // Bare world: chat untouched, le falsely corrected.
        Assert.Equal(2L, report.BareRef);
        Assert.Equal(1L, report.Untouched);
        Assert.Equal(1L, report.FalseCorrections);
    }

    [Fact]
    public void TopListsNameTheOffenders()
    {
        var report = Evaluate("le chat marché était à côté.", Policy());

        Assert.Contains(("était", 1L), report.TopMissed);
        Assert.Contains(("côté", 1L), report.TopWrongForm);
        Assert.Contains(("le", 1L), report.TopFalseCorrections);
    }

    [Fact]
    public void DerivedRatesMatchTheCounts()
    {
        var report = Evaluate("le chat marché était à côté.", Policy());

        Assert.Equal(2.0 / 4.0, report.RestorationRecall, 6);
        Assert.Equal(1.0 / 2.0, report.FalseCorrectionRate, 6);
        // Correct = Restored (2) + Untouched (1) over 6 tokens.
        Assert.Equal(3.0 / 6.0, report.WordAccuracy, 6);

        // Precision is the headline: of the 4 emitted corrections (là, marché,
        // à, coté) two were right — restored marché and à. The two wrong ones
        // are the false correction (là) and the wrong form (coté).
        Assert.Equal(4L, report.EmittedCorrections);
        Assert.Equal(2L, report.Corruptions);
        Assert.Equal(2.0 / 4.0, report.Precision, 6);
    }

    [Fact]
    public void EmptyDenominatorRatesReadAsNaNNotZero()
    {
        // A pure-bare reference the policy never touches: no accents needed, none
        // emitted. Recall and precision are "not measured" (NaN), not a flat 0%
        // that would read as a perfect-or-failing score.
        var report = Evaluate("chat chien.", Policy());

        Assert.Equal(0L, report.AccentedRef);
        Assert.Equal(0L, report.EmittedCorrections);
        Assert.True(double.IsNaN(report.RestorationRecall));
        Assert.True(double.IsNaN(report.Precision));
        Assert.Equal(0.0, report.FalseCorrectionRate, 6); // bare words exist → measured
    }

    [Fact]
    public void BreaksDownEmittedCorrectionsByStage()
    {
        // Every ScriptedPolicy rule reports LexicalGate, so the four emitted
        // corrections land in one stage: two correct (marché, à), two wrong
        // (the false correction là, the wrong form coté). Missed/untouched
        // tokens never acted, so they contribute nothing to the breakdown.
        var report = Evaluate("le chat marché était à côté.", Policy());

        Assert.True(report.ByStage.TryGetValue(CorrectionReason.LexicalGate, out var gate));
        Assert.Equal(4L, gate!.Acted);
        Assert.Equal(2L, gate.Correct);
        Assert.Equal(2L, gate.Wrong);
    }

    [Fact]
    public void ChainsThePreviousOutputWithinTheSentence()
    {
        var policy = Policy();
        Evaluate("le chat marché était à côté.", policy);

        // "était" was a miss, so its output stayed "etait"; that lowercased
        // output is the prev "a" saw — proof the chain carries OUTPUT, not the
        // reference token.
        Assert.Equal("etait", policy.LastPrevForA);
    }

    [Fact]
    public void TypingSimulationStripsAccentsKeepingCase()
    {
        // The typist produces "Francais" from "Français": accent gone, capital
        // kept. A policy that only knows the stripped key still gets to fire.
        var policy = new ScriptedPolicy(new Dictionary<string, (string, string?)>
        {
            ["Francais"] = ("Français", null),
        });

        var report = RestorationEvaluator.Evaluate(new StringReader("Français."), policy);

        Assert.Equal(1L, report.AccentedRef);
        Assert.Equal(1L, report.Restored);
    }

    // Fires only when there is no left context — to prove the prev resets at a
    // sentence boundary.
    private sealed class OnlyAtSentenceStartPolicy : ICorrectionPolicy
    {
        public CorrectionDecision? Evaluate(string word, IReadOnlyList<string> leftContext) =>
            word == "etait" && leftContext.Count == 0
                ? new CorrectionDecision(word, "était", CorrectionReason.LexicalGate)
                : null;
    }

    [Fact]
    public void PreviousWordResetsAtSentenceStart()
    {
        // « x était. était y. » — "était" appears once mid-sentence (prev "x")
        // and once sentence-initial (prev null). A policy that only fires when
        // prev is null fixes exactly the second: proof the period reset context.
        var report = RestorationEvaluator.Evaluate(
            new StringReader("x était. était y."), new OnlyAtSentenceStartPolicy());

        Assert.Equal(2L, report.AccentedRef); // both "était" are accented refs
        Assert.Equal(1L, report.Restored);    // only the sentence-initial one
        Assert.Equal(1L, report.Missed);      // the mid-sentence one (prev "x")
    }

    [Fact]
    public void TokenCapStopsEarly()
    {
        var policy = Policy();
        var report = RestorationEvaluator.Evaluate(
            new StringReader("le chat marché était à côté."),
            policy,
            new EvaluatorOptions { MaxTokens = 3 });

        Assert.Equal(3L, report.TotalTokens); // le, chat, marché — then stop.
    }
}
