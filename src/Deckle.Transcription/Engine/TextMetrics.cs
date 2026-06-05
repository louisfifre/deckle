using System.Text.RegularExpressions;

namespace Deckle.Transcription;

// ── TextMetrics ─────────────────────────────────────────────────────────────
//
// Whitespace-tolerant word counter. Every maximal run of non-whitespace
// counts as one word — resilient to multiple spaces, tabs, CRLFs, mixed
// punctuation. No locale/NLP assumptions.
//
// Used by `TranscriptionEngine` to fill the `text_words` field of the
// `LatencyRecorded`, `CorpusAsrRecorded` and `CorpusRewriteRecorded`
// events. Carry-over from wave 6: utility formerly hosted by `Deckle.Logging`
// with the payloads, relocated next to its only business consumer.
internal static class TextMetrics
{
    private static readonly Regex _token = new(@"\S+", RegexOptions.Compiled);

    public static int CountWords(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : _token.Matches(text).Count;
}
