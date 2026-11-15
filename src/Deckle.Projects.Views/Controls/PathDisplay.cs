namespace Deckle.Projects.Views;

// A folder path shortened for a single line. A TextBlock can only trim the end,
// and the end of a path is the folder's name, the one part that tells two
// projects apart. So the middle goes and the root and the last two segments stay:
// "C:\...\sample-projects\deckle". The full path belongs in a tooltip.
internal static class PathDisplay
{
    private const char Separator = '\\';

    public static string Shorten(string path, int maxLength = 44)
    {
        if (path.Length <= maxLength) return path;

        string[] parts = path.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3) return path;

        string root = parts[0];
        string tail = parts[^2] + Separator + parts[^1];
        string candidate = root + Separator + "…" + Separator + tail;
        // A tail that is long by itself: keep only the folder's name.
        return candidate.Length <= maxLength ? candidate : "…" + Separator + parts[^1];
    }
}
