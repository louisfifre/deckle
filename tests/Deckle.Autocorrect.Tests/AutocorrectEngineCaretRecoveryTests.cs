using Deckle.Autocorrect;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "integration")]
public sealed class AutocorrectEngineCaretRecoveryTests
{
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

        Assert.True(SpinWait.SpinUntil(() => reader.ReadCount >= 1, 2_000));
        harness.Host.Drain();
        Assert.True(SpinWait.SpinUntil(() => reader.ReadCount >= 2, 2_000));
        Assert.True(SpinWait.SpinUntil(() => harness.Host.HasPendingDrain, 2_000));
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
