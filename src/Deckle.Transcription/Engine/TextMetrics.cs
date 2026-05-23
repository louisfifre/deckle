using System.Text.RegularExpressions;

namespace Deckle.Transcription;

// ── TextMetrics ─────────────────────────────────────────────────────────────
//
// Whitespace-tolerant word counter. Every maximal run of non-whitespace
// counts as one word — resilient to multiple spaces, tabs, CRLFs, mixed
// punctuation. No locale/NLP assumptions.
//
// Used by `WhispEngine` to fill the `text_words` field of the
// `LatencyRecorded` and `CorpusRecorded` events. Carry-over de la vague
// 6 : utilitaire jadis hébergé par `Deckle.Logging` avec les payloads,
// relocalisé aux côtés de son seul consommateur métier.
internal static class TextMetrics
{
    private static readonly Regex _token = new(@"\S+", RegexOptions.Compiled);

    public static int CountWords(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : _token.Matches(text).Count;
}
