using System.Text.RegularExpressions;

namespace Deckle.Transcription;

// Word counter for telemetry and logs: every run of non-whitespace is one
// word, with no locale or NLP assumptions.
internal static class TextMetrics
{
    private static readonly Regex _token = new(@"\S+", RegexOptions.Compiled);

    public static int CountWords(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : _token.Matches(text).Count;
}
