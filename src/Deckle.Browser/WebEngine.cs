namespace Deckle.Browser;

// The web engine (the WebView2 runtime) a page runs in. One older than the
// minimum no longer gets the security fixes the browser relies on.
public static class WebEngine
{
    public const int MinimumMajor = 140;

    /// <summary>True when the major version of a runtime version string such as
    /// "141.0.3537.57" is below the minimum. A string that cannot be read is
    /// not called out of date: nothing is known about it.</summary>
    public static bool IsOutOfDate(string? versionString) =>
        Major(versionString) is { } major && major < MinimumMajor;

    public static int? Major(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString)) return null;
        string first = versionString.Trim().Split('.')[0];
        return int.TryParse(first, out int major) && major >= 0 ? major : null;
    }
}
