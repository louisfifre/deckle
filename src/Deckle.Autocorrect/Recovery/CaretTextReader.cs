using Deckle.Core;

namespace Deckle.Autocorrect;

public interface ICaretTextReader
{
    bool TryReadStable(out FocusedCaretText text, out string reason);
}

// UIA is intentionally sampled away from the input and UI threads. Two equal
// reads prove a stable snapshot; unsupported password metadata, a selection,
// focus drift, or any text mutation abstains without exposing text to logs.
public sealed class UIAutomationCaretTextReader : ICaretTextReader
{
    private const int MaxCharacters = 1024;
    private static readonly TimeSpan InitialSettle = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan VerificationGap = TimeSpan.FromMilliseconds(75);

    public bool TryReadStable(out FocusedCaretText text, out string reason)
    {
        text = default;
        Thread.Sleep(InitialSettle);

        if (!UIAutomation.TryReadFocusedTextBeforeCaret(
                MaxCharacters,
                out FocusedCaretText first,
                out reason))
            return false;

        Thread.Sleep(VerificationGap);
        if (!UIAutomation.TryReadFocusedTextBeforeCaret(
                MaxCharacters,
                out FocusedCaretText second,
                out reason))
            return false;

        if (!string.Equals(first.TargetIdentity, second.TargetIdentity, StringComparison.Ordinal))
        {
            reason = CaretTextReadReasons.TargetChanged;
            return false;
        }
        if (!string.Equals(first.TextBeforeCaret, second.TextBeforeCaret, StringComparison.Ordinal)
            || first.ReachedDocumentStart != second.ReachedDocumentStart)
        {
            reason = CaretTextReadReasons.TextChanged;
            return false;
        }

        text = first;
        reason = CaretTextReadReasons.Accepted;
        return true;
    }
}

public static class CaretTextReadReasons
{
    public const string Accepted = "accepted";
    public const string TargetChanged = "target_changed";
    public const string TextChanged = "text_changed";
}
