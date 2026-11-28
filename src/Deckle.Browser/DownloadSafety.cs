namespace Deckle.Browser;

// Whether a downloaded file can run programs on the computer, judged by its
// name's extension alone.
public static class DownloadSafety
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".jar", ".scr", ".com", ".lnk", ".reg", ".hta", ".dll",
    };

    public static bool IsExecutable(string fileName)
    {
        string name = fileName.Trim().TrimEnd('.', ' ');
        int dot = name.LastIndexOf('.');
        return dot >= 0 && Executable.Contains(name[dot..]);
    }
}
