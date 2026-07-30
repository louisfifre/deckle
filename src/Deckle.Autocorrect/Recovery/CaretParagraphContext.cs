namespace Deckle.Autocorrect;

// Extracts the paragraph immediately before the return that just closed it.
// The closing return stays outside Text because the injector removes and
// recreates that gesture separately.
public static class CaretParagraphContext
{
    public const int MaxParagraphLength = 4096;

    public static CaretParagraphContextResult ExtractClosed(
        string textBeforeCaret,
        bool reachedDocumentStart)
    {
        if (string.IsNullOrEmpty(textBeforeCaret))
            return Reject(CaretParagraphContextReasons.EmptyText);

        int end = textBeforeCaret.Length;
        if (textBeforeCaret[^1] == '\n')
        {
            end--;
            if (end > 0 && textBeforeCaret[end - 1] == '\r') end--;
        }
        else if (textBeforeCaret[^1] is '\r' or '\u2028' or '\u2029')
        {
            end--;
        }
        else
        {
            return Reject(CaretParagraphContextReasons.ClosingReturnNotFound);
        }

        int start = -1;
        CaretParagraphBoundary boundary = CaretParagraphBoundary.None;
        for (int index = end - 1; index >= 0; index--)
        {
            if (textBeforeCaret[index] is not ('\r' or '\n' or '\u2028' or '\u2029'))
                continue;
            start = index + 1;
            boundary = CaretParagraphBoundary.HardReturn;
            break;
        }

        if (start < 0)
        {
            if (!reachedDocumentStart)
                return Reject(CaretParagraphContextReasons.BoundaryNotFound);
            start = 0;
            boundary = CaretParagraphBoundary.DocumentStart;
        }

        string paragraph = textBeforeCaret[start..end];
        if (string.IsNullOrWhiteSpace(paragraph))
            return Reject(CaretParagraphContextReasons.EmptyText);
        if (paragraph.Length > MaxParagraphLength)
            return Reject(CaretParagraphContextReasons.ParagraphTooLong);

        return new CaretParagraphContextResult(
            true,
            paragraph,
            start,
            boundary,
            CaretParagraphContextReasons.Accepted);
    }

    private static CaretParagraphContextResult Reject(string reason) =>
        new(false, string.Empty, -1, CaretParagraphBoundary.None, reason);
}

public enum CaretParagraphBoundary
{
    None,
    DocumentStart,
    HardReturn,
}

public readonly record struct CaretParagraphContextResult(
    bool Available,
    string Text,
    int StartIndex,
    CaretParagraphBoundary Boundary,
    string Reason);

public static class CaretParagraphContextReasons
{
    public const string Accepted = "accepted";
    public const string EmptyText = "empty_text";
    public const string ClosingReturnNotFound = "closing_return_not_found";
    public const string BoundaryNotFound = "boundary_not_found";
    public const string ParagraphTooLong = "paragraph_too_long";
}
