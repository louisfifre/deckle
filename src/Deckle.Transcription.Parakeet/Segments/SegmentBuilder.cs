using System.Text;

namespace Deckle.Transcription.Parakeet.Segments;

// ── SegmentBuilder ───────────────────────────────────────────────────────────
//
// Turns the flat token stream of the transducer decoder into TranscriptionSegment
// values, the unit the rest of Deckle (sink, logs, history) already speaks.
//
// Parakeet has no segment notion of its own, unlike Whisper: it emits tokens with
// the encoder frame they were emitted on. A segment is therefore cut by rule:
//   • after a sentence-ending mark (. ? ! …), but only when the next token starts a
//     new word, so "3.5" and "e.g." stay in one piece;
//   • before a word that would push the segment past MaxSegmentCs, so a run with no
//     punctuation still yields segments a UI can show (cut on a word boundary,
//     never inside a word).
//
// Time. One encoder frame is 80 ms: 10 ms mel hop x subsampling 8. Segment times
// are in centiseconds like Whisper's, so a frame is 8 cs. T0 is the first token's
// frame; T1 is the end of the last token's frame (frame + 1), since the token was
// emitted somewhere inside that frame and a zero-length segment reads as a bug.
// offsetCs shifts everything, so chunks of one long recording keep one timeline.
internal static class SegmentBuilder
{
    /// <summary>Centiseconds per encoder frame (10 ms hop x subsampling 8 = 80 ms).</summary>
    public const int CentisecondsPerFrame = 8;

    /// <summary>Default longest segment, 30 s, Whisper's window and a readable upper bound.</summary>
    public const long DefaultMaxSegmentCs = 3000;

    private const char WordMarker = '\u2581';

    public static IReadOnlyList<TranscriptionSegment> Build(
        IReadOnlyList<DecodedToken> tokens,
        long offsetCs = 0,
        long maxSegmentCs = DefaultMaxSegmentCs)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetCs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSegmentCs);

        var segments = new List<TranscriptionSegment>();
        var text = new StringBuilder();
        int count = 0;
        int firstFrame = 0, lastFrame = 0;
        double probSum = 0;
        float probMin = 1f;

        void Flush()
        {
            string trimmed = text.ToString().Trim();
            if (count > 0 && trimmed.Length > 0)
            {
                segments.Add(new TranscriptionSegment(
                    trimmed,
                    offsetCs + (long)firstFrame * CentisecondsPerFrame,
                    offsetCs + (long)(lastFrame + 1) * CentisecondsPerFrame,
                    (float)(probSum / count),
                    NoSpeechProb: 0f,
                    TextTokens: count,
                    MinTokenProbability: probMin));
            }
            text.Clear();
            count = 0;
            probSum = 0;
            probMin = 1f;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            DecodedToken token = tokens[i];
            string piece = token.Text.Replace(WordMarker, ' ');
            bool startsWord = piece.Length > 0 && piece[0] == ' ';

            // Cut before a word that would overflow, never inside one.
            if (count > 0 && startsWord &&
                (long)(token.Frame + 1 - firstFrame) * CentisecondsPerFrame > maxSegmentCs)
            {
                Flush();
            }

            if (count == 0) firstFrame = token.Frame;
            lastFrame = token.Frame;
            text.Append(piece);
            count++;
            probSum += token.Probability;
            probMin = Math.Min(probMin, token.Probability);

            if (EndsSentence(piece) && (i + 1 == tokens.Count || StartsWord(tokens[i + 1].Text)))
            {
                Flush();
            }
        }

        Flush();
        return segments;
    }

    private static bool StartsWord(string rawPiece) =>
        rawPiece.Length > 0 && (rawPiece[0] == WordMarker || rawPiece[0] == ' ');

    private static bool EndsSentence(string piece)
    {
        string trimmed = piece.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] is '.' or '?' or '!' or '\u2026';
    }
}
