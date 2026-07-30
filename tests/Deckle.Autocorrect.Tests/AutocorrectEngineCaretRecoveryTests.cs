using Deckle.Autocorrect;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "integration")]
public sealed class AutocorrectEngineCaretRecoveryTests
{
    private const int BackgroundWorkTimeoutMilliseconds = 10_000;

    [Fact]
    public void TerminalPunctuationRecoversTheExactPhraseAfterAnEditReset()
    {
        const string literal = "Il y a une seul erreur.";
        var reader = new StableCaretReader("Titre\n" + literal);
        FrequencyLexicon french = FrequencyLexicon.LoadTsv(new StringReader(
            "il\t100\ny\t100\na\t100\nune\t100\nun\t90\nseul\t80\nseule\t70\nerreur\t100\n"));
        var gender = new GenderVariantProbe(french);
        using var harness = new AutocorrectEngineHarness(
            french: french,
            reranker: new ChooseSeuleWholeSentenceReranker(),
            probe: gender,
            wholeSentenceProbe: gender,
            caretTextReader: reader);
        harness.Settings.Apps["codex"] = true;
        harness.Prober.Surface = AutocorrectEngineHarness.Editable("codex");
        Assert.True(harness.Start());

        harness.Type("Il y a une seul erreur");
        harness.Backspace();
        harness.Type("r.");

        Assert.True(SpinWait.SpinUntil(
            () => reader.ReadCount >= 1,
            BackgroundWorkTimeoutMilliseconds));
        harness.Host.Drain();
        Assert.True(SpinWait.SpinUntil(
            () => reader.ReadCount >= 2,
            BackgroundWorkTimeoutMilliseconds));
        Assert.True(SpinWait.SpinUntil(
            () => harness.Host.HasPendingDrain,
            BackgroundWorkTimeoutMilliseconds));
        harness.Host.Drain();

        Assert.Equal("Il y a une seule erreur.", harness.VisibleText);
        Assert.Equal(
            ("seul erreur.", "seule erreur."),
            Assert.Single(harness.Injector.Calls));
        Assert.Single(harness.Applied);
    }

    [Fact]
    public void TerminalPunctuationRecoversEvenWhenNoWordWasObservedBeforeIt()
    {
        var reader = new StableCaretReader("Titre\nla.");
        using var harness = new AutocorrectEngineHarness(
            reranker: new ChooseLaAccentReranker(),
            probe: new LaAccentProbe(),
            caretTextReader: reader);
        harness.Prober.Surface = AutocorrectEngineHarness.Editable();
        harness.Start();

        harness.Type("la");
        harness.Host.RaisePointer();
        harness.Type(".");

        Assert.True(SpinWait.SpinUntil(
            () => reader.ReadCount >= 1,
            BackgroundWorkTimeoutMilliseconds));
        harness.Host.Drain();
        Assert.True(SpinWait.SpinUntil(
            () => reader.ReadCount >= 2,
            BackgroundWorkTimeoutMilliseconds));
        Assert.True(SpinWait.SpinUntil(
            () => harness.Host.HasPendingDrain,
            BackgroundWorkTimeoutMilliseconds));
        harness.Host.Drain();

        Assert.Equal("là.", harness.VisibleText);
        Assert.Equal(("la.", "là."), Assert.Single(harness.Injector.Calls));
    }

    private sealed class StableCaretReader : ICaretTextReader
    {
        private readonly string _text;
        private int _readCount;

        public StableCaretReader(string text) => _text = text;

        public int ReadCount => Volatile.Read(ref _readCount);

        public bool TryReadStable(out FocusedCaretText text, out string reason)
        {
            Interlocked.Increment(ref _readCount);
            text = new FocusedCaretText(
                _text,
                ReachedDocumentStart: false,
                MovedCharacters: _text.Length,
                ProcessId: 42,
                ControlType: 50004,
                NativeWindowHandle: 0,
                ForegroundWindow: 1234,
                RuntimeId: "42.1.2",
                Pattern: "text_selection");
            reason = CaretTextReadReasons.Accepted;
            return true;
        }
    }

    private sealed class ChooseLaAccentReranker : ISentenceReranker
    {
        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates) =>
            new("là", Array.Empty<RerankCandidateScore>(), 2.0, 1.0, null);
    }

    private sealed class ChooseSeuleWholeSentenceReranker
        : ISentenceReranker, IWholeSentenceReranker
    {
        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates) =>
            throw new InvalidOperationException("The exact phrase must use one global verdict.");

        public RerankOutcome RerankSentence(ClosedSentenceTransaction transaction)
        {
            SentenceEditCandidate winner = transaction.Edits.Single(candidate =>
                candidate.Replacement == "seule");
            return new RerankOutcome(
                winner.Replacement,
                Array.Empty<RerankCandidateScore>(),
                Margin: 2.0,
                Threshold: 1.0,
                AbstainReason: null)
            {
                ChosenSlotIndex = winner.SlotIndex,
            };
        }
    }

    private sealed class LaAccentProbe : IAmbiguityProbe
    {
        private static readonly AccentVariant[] Candidates =
        {
            new("la", 9000.0),
            new("là", 50.0),
        };

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            string.Equals(word, "la", StringComparison.OrdinalIgnoreCase)
                ? Candidates
                : Array.Empty<AccentVariant>();

        public IReadOnlyList<AccentVariant> SentenceCandidates(
            string word,
            bool includeTypedLiteral) => AmbiguousCandidates(word);
    }
}
