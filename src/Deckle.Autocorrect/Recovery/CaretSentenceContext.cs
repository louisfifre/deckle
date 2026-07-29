using Deckle.Core;

namespace Deckle.Autocorrect;

// Extracts the exact sentence-shaped suffix immediately before a caret. It does
// not decide whether UIA is trustworthy and does not grant correction rights;
// callers first verify the focused target and snapshot stability.
public static class CaretSentenceContext
{
    public const int MaxSentenceLength = 512;

    public static CaretSentenceContextResult Extract(
        string textBeforeCaret,
        bool reachedDocumentStart)
    {
        if (string.IsNullOrEmpty(textBeforeCaret))
            return Reject(CaretSentenceContextReasons.EmptyText);

        int start = reachedDocumentStart
            ? SkipHorizontalWhitespace(textBeforeCaret, 0)
            : -1;
        CaretSentenceBoundary boundary = reachedDocumentStart
            ? CaretSentenceBoundary.DocumentStart
            : CaretSentenceBoundary.None;

        for (int index = 0; index < textBeforeCaret.Length; index++)
        {
            char current = textBeforeCaret[index];
            if (IsHardReturn(current))
            {
                int next = index + 1;
                while (next < textBeforeCaret.Length
                    && (IsHardReturn(textBeforeCaret[next])
                        || IsHorizontalWhitespace(textBeforeCaret[next])))
                {
                    next++;
                }
                start = next;
                boundary = CaretSentenceBoundary.HardReturn;
                index = next - 1;
                continue;
            }

            if (!IsTerminalPunctuation(current)
                || index + 1 >= textBeforeCaret.Length
                || !IsHorizontalWhitespace(textBeforeCaret[index + 1]))
            {
                continue;
            }

            int sentenceStart = SkipHorizontalWhitespace(textBeforeCaret, index + 1);
            start = sentenceStart;
            boundary = CaretSentenceBoundary.TerminalPunctuation;
            index = sentenceStart - 1;
        }

        if (start < 0)
            return Reject(CaretSentenceContextReasons.BoundaryNotFound);

        string sentence = textBeforeCaret[start..];
        if (string.IsNullOrWhiteSpace(sentence))
            return Reject(CaretSentenceContextReasons.EmptyText);
        if (sentence.Length > MaxSentenceLength)
            return Reject(CaretSentenceContextReasons.SentenceTooLong);

        return new CaretSentenceContextResult(
            true,
            sentence,
            start,
            boundary,
            CaretSentenceContextReasons.Accepted);
    }

    private static int SkipHorizontalWhitespace(string text, int start)
    {
        int index = start;
        while (index < text.Length && IsHorizontalWhitespace(text[index])) index++;
        return index;
    }

    private static bool IsHorizontalWhitespace(char value) =>
        char.IsWhiteSpace(value) && !IsHardReturn(value);

    private static bool IsHardReturn(char value) =>
        value is '\r' or '\n' or '\u2028' or '\u2029';

    internal static bool IsTerminalPunctuation(char value) =>
        value is '.' or '!' or '?' or '…';

    private static CaretSentenceContextResult Reject(string reason) =>
        new(false, string.Empty, -1, CaretSentenceBoundary.None, reason);
}

public readonly record struct VerifiedCaretSentence(
    FocusedCaretText Snapshot,
    string Text)
{
    public bool Matches(FocusedCaretText current)
    {
        if (!string.Equals(
                Snapshot.TargetIdentity,
                current.TargetIdentity,
                StringComparison.Ordinal))
            return false;

        CaretSentenceContextResult extracted = CaretSentenceContext.Extract(
            current.TextBeforeCaret,
            current.ReachedDocumentStart);
        return extracted.Available
            && string.Equals(extracted.Text, Text, StringComparison.Ordinal);
    }
}

public enum CaretSentenceBoundary
{
    None,
    DocumentStart,
    HardReturn,
    TerminalPunctuation,
}

public readonly record struct CaretSentenceContextResult(
    bool Available,
    string Text,
    int StartIndex,
    CaretSentenceBoundary Boundary,
    string Reason);

public static class CaretSentenceContextReasons
{
    public const string Accepted = "accepted";
    public const string EmptyText = "empty_text";
    public const string BoundaryNotFound = "boundary_not_found";
    public const string SentenceTooLong = "sentence_too_long";
}
