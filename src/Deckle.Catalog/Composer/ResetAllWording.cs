namespace Deckle.Catalog;

// The body of the "Reset this page?" dialog every settings page shows: the plain
// sentence first (the values go back to their defaults), then the page's own
// detail, one blank line apart, when it has one. Kept apart from the header so
// the join is a pure function a test can pin.
public static class ResetAllWording
{
    public static string Body(string intro, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? intro : intro + "\n\n" + detail.Trim();
}
