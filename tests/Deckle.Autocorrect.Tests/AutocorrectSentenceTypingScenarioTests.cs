using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Sentence-stage behavior through the whole keyboard pipeline. The background
// lane remains real; the fake host only replaces the Windows message pump and
// delivers completed verdicts on the test thread when PumpDrain is called.
[Trait("Category", "integration")]
public sealed class AutocorrectSentenceTypingScenarioTests
{
    [Fact]
    public void ClosedSentenceVerdictRepairsTheVisibleSentenceTail()
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        using var h = Harness(
            french,
            probe,
            new ChoosingSentenceReranker("là"));

        h.Type("Je suis la.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là.", h.VisibleText);
        CorrectionDecision applied = Assert.Single(h.Applied);
        Assert.Equal(CorrectionReason.SentenceReranker, applied.Reason);
    }

    [Fact]
    public void ContinuousForwardTypingRemainsInTheExactLateRewriteTail()
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);

        h.Type("Je suis la.");
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        h.Type("x");
        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là.x", h.VisibleText);
        Assert.Equal(CorrectionReason.SentenceReranker, Assert.Single(h.Applied).Reason);
        Assert.Equal(("la.x", "là.x"), Assert.Single(h.Injector.Calls));
    }

    [Fact]
    public void FollowingSentencePartialCanStayVisibleWhileThePriorVerdictLands()
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là", "suite");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);

        h.Type("Je suis la.");
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        h.Type(" Suite");
        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là. Suite", h.VisibleText);
        Assert.Equal(("la. Suite", "là. Suite"), Assert.Single(h.Injector.Calls));
    }

    [Fact]
    public void FollowingCommittedWordsStayVisibleWhileThePriorVerdictLands()
    {
        FrequencyLexicon french = Lexicon(
            "je", "suis", "la", "là", "suite", "continue", "déjà");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);

        h.Type("Je suis la.");
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        h.Type(" Suite continue déjà");
        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là. Suite continue déjà", h.VisibleText);
        Assert.Equal(
            ("la. Suite continue déjà", "là. Suite continue déjà"),
            Assert.Single(h.Injector.Calls));
    }

    [Fact]
    public void NaturalClausePunctuationKeepsTheTailExactUntilSentenceClosure()
    {
        FrequencyLexicon french = Lexicon(
            "je", "reste", "la", "là", "parce", "qu", "il", "pleut");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        using var h = Harness(french, probe, new ChoosingSentenceReranker("là"));

        h.Type("Je reste la, parce qu'il pleut.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je reste là, parce qu'il pleut.", h.VisibleText);
        Assert.Equal(
            ("la, parce qu'il pleut.", "là, parce qu'il pleut."),
            Assert.Single(h.Injector.Calls));
    }

    [Fact]
    public void ClosingSpaceCanArriveBeforeVerdictWithoutExpiringTheSentence()
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);

        h.Type("Je suis la.");
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        h.Type(" ");
        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là. ", h.VisibleText);
    }

    [Theory]
    [InlineData("backspace")]
    [InlineData("enter")]
    [InlineData("navigation")]
    [InlineData("shortcut")]
    [InlineData("pointer")]
    [InlineData("focus")]
    public void DiscontinuousGestureExpiresTheLateVerdict(string gesture)
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);

        h.Type("Je suis la.", interKeyMs: 35);
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        switch (gesture)
        {
            case "backspace": h.Backspace(); break;
            case "enter": h.Enter(); break;
            case "navigation": h.NavigateLeft(); break;
            case "shortcut": h.ControlShortcut('a'); break;
            case "pointer": h.Pointer(); break;
            case "focus": h.RefocusOn(AutocorrectEngineHarness.Editable("codex")); break;
        }

        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));
        Assert.Empty(h.Applied);
        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void ForeignAutocorrectExpiresALateVerdictBeforeBlindSendInputCanCorruptTheTail()
    {
        FrequencyLexicon french = Lexicon("je", "suis", "la", "là");
        var probe = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var reranker = new BlockingSentenceReranker("là");
        using var h = Harness(french, probe, reranker);
        h.Injector.VerifySurfaceSuffix = false;

        h.Type("Je suis la.");
        Assert.True(reranker.Started.Wait(
            TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        h.ForeignReplaceSuffix("la.", "là.");
        reranker.Release.Set();
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là.", h.VisibleText);
        Assert.Empty(h.Applied);
        Assert.Empty(h.Injector.Calls);
    }

    [Fact]
    public void AmbiguousOneKeySlipIsDeferredToTheClosedSentenceJudge()
    {
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(
            "avant\t3000\nça\t8000\nallait\t2500\nvraiment\t1200\nun\t5000\n"
            + "peu\t1586.96\npu\t366.22\npur\t44.59\nmieux\t900\n"));
        var diacritics = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var typo = new ConservativeTypoCorrector(french);
        var policy = new CompositeCorrectionPolicy(diacritics, typo);
        var probe = new CompositeAmbiguityProbe(diacritics, typo);
        using var h = new AutocorrectEngineHarness(
            policy,
            french: french,
            reranker: new ChoosingSentenceReranker("peu"),
            probe: probe);
        h.Settings.Apps["codex"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(h.Start());

        h.Type("avant ça allait vraiment un pru mieux.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("avant ça allait vraiment un peu mieux.", h.VisibleText);
        Assert.Equal(CorrectionReason.SentenceReranker, Assert.Single(h.Applied).Reason);
    }

    [Fact]
    public void CoherentHandOffsetIsDeferredToTheClosedSentenceJudge()
    {
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(
            "la\t8000\ndate\t5000\nqui\t9000\nserait\t4000\nplus\t6000\nimportante\t1200\n"));
        var diacritics = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var typo = new ConservativeTypoCorrector(french);
        var policy = new CompositeCorrectionPolicy(diacritics, typo);
        var probe = new CompositeAmbiguityProbe(diacritics, typo);
        using var h = new AutocorrectEngineHarness(
            policy,
            french: french,
            reranker: new ChoosingSentenceReranker("qui"),
            probe: probe);
        h.Settings.Apps["codex"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(h.Start());

        h.Type("la date qio serait la plus importante.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("la date qui serait la plus importante.", h.VisibleText);
        Assert.Equal(CorrectionReason.SentenceReranker, Assert.Single(h.Applied).Reason);
    }

    [Fact]
    public void ShortTwoSlipFaultIsDeferredToTheClosedSentenceJudge()
    {
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(
            "ça\t8000\nallait\t4000\nun\t9000\npeu\t1500\nmieux\t900\n"));
        var diacritics = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var typo = new ConservativeTypoCorrector(french);
        var policy = new CompositeCorrectionPolicy(diacritics, typo);
        var probe = new CompositeAmbiguityProbe(diacritics, typo);
        using var h = new AutocorrectEngineHarness(
            policy,
            french: french,
            reranker: new ChoosingSentenceReranker("mieux"),
            probe: probe);
        h.Settings.Apps["codex"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(h.Start());

        h.Type("ça allait un peu miru.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("ça allait un peu mieux.", h.VisibleText);
        Assert.Equal(CorrectionReason.SentenceReranker, Assert.Single(h.Applied).Reason);
    }

    [Theory]
    [InlineData(
        "avant ça allait vraiment un pru mieux.",
        "avant ça allait vraiment un peu mieux.",
        "peu")]
    [InlineData(
        "la date qio serait la plus importante.",
        "la date qui serait la plus importante.",
        "qui")]
    [InlineData(
        "ça allait un peu miru finalement.",
        "ça allait un peu mieux finalement.",
        "mieux")]
    public void PackagedCompositionRepairsCollectedContextualResidue(
        string typed,
        string expected,
        string chosen)
    {
        using var h = PackagedHarness(new ChoosingSentenceReranker(chosen));

        h.Type(typed);
        Assert.True(h.PumpUntil(() => h.VisibleText == expected, TimeSpan.FromSeconds(2)));

        Assert.Equal(expected, h.VisibleText);
        Assert.Contains(h.Applied, correction =>
            correction.Reason == CorrectionReason.SentenceReranker
            && correction.Replacement == chosen);
    }

    [Fact]
    public void SentenceRewriteTailUsesTheSeparatorAlreadyRepairedOnScreen()
    {
        FrequencyLexicon french = Lexicon(
            "je", "suis", "la", "là", "parce", "qu", "il", "pleut");
        var diacritics = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var family = new MistouchFamilyRecord(
            "sub ;→'", MistouchFamilyKinds.BoundaryApostrophe);
        using var h = new AutocorrectEngineHarness(
            diacritics,
            french: french,
            reranker: new ChoosingSentenceReranker("là"),
            probe: diacritics,
            mistouchFamilies: [family]);
        h.Settings.Apps["codex"] = true;
        h.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(h.Start());

        h.Type("Je suis la, parce qu;il pleut.");
        Assert.True(h.PumpDrain(TimeSpan.FromSeconds(2)));

        Assert.Equal("Je suis là, parce qu'il pleut.", h.VisibleText);
        Assert.Empty(h.InjectionFailures);
        Assert.Contains(h.Injector.Calls, call =>
            call == ("la, parce qu'il pleut.", "là, parce qu'il pleut."));
    }

    private static AutocorrectEngineHarness Harness(
        FrequencyLexicon french,
        IAmbiguityProbe probe,
        ISentenceReranker reranker)
    {
        var policy = new DiacriticsRestorer(french, null, AccentIndex.Build(french));
        var harness = new AutocorrectEngineHarness(
            policy,
            french: french,
            reranker: reranker,
            probe: probe);
        harness.Settings.Apps["codex"] = true;
        harness.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(harness.Start());
        return harness;
    }

    private static AutocorrectEngineHarness PackagedHarness(ISentenceReranker reranker)
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        FrequencyLexicon french = FrequencyLexicon.LoadTsvGz(Path.Combine(
            dataDir, AutocorrectLexiconArtifacts.FrenchFileName));
        var english = new GlobalEnglishLexicon(
            AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(dataDir));
        AccentIndex index = AccentIndex.Build(french);
        AutocorrectPolicySet policies = AutocorrectPolicySet.Create(
            french,
            english,
            index,
            BigramPairDisambiguator.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.PairBigramsFrenchFileName)),
            verbs: VerbMorphology.LoadTsvGz(Path.Combine(
                dataDir, AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName)));
        var harness = new AutocorrectEngineHarness(
            policies.Policy,
            french: french,
            english: english,
            reranker: reranker,
            probe: policies.AmbiguityProbe);
        harness.Settings.Apps["codex"] = true;
        harness.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(harness.Start());
        return harness;
    }

    private static FrequencyLexicon Lexicon(params string[] words)
    {
        string rows = string.Join(
            '\n',
            words.Select((word, index) => $"{word}\t{1000 - index}"));
        return FrequencyLexicon.LoadTsv(new StringReader(rows));
    }

    private sealed class ChoosingSentenceReranker(string chosen) : ISentenceReranker
    {
        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates)
        {
            string verdict = candidates.Any(candidate => candidate.Form == chosen)
                ? chosen
                : sentence[slotIndex];
            return new(
                verdict,
                candidates.Select(candidate => new RerankCandidateScore(candidate.Form, 1.0)).ToArray(),
                Margin: 2.0,
                Threshold: 1.0,
                AbstainReason: null);
        }
    }

    private sealed class BlockingSentenceReranker(string chosen) : ISentenceReranker, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates)
        {
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(2));
            return new RerankOutcome(
                chosen,
                candidates.Select(candidate => new RerankCandidateScore(candidate.Form, 1.0)).ToArray(),
                Margin: 2.0,
                Threshold: 1.0,
                AbstainReason: null);
        }

        public void Dispose()
        {
            Release.Set();
            Started.Dispose();
            Release.Dispose();
        }
    }
}
