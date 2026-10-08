using System.Diagnostics;
using System.Globalization;

namespace Deckle.Orchestration;

// The label an entry gets when it is added. Prefers the file description
// stamped in the executable ("Notepad", "Steam") because that is the name
// Task Manager and the Start menu show; falls back to a window title, then to
// the executable stem with a capital first letter.
internal static class AppDisplayName
{
    public static string For(string? executablePath, string? windowTitle, string processName)
    {
        string? description = TryFileDescription(executablePath);
        if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        if (!string.IsNullOrWhiteSpace(windowTitle)) return windowTitle.Trim();
        return Humanize(processName);
    }

    // "anytype" → "Anytype". Kept simple: the stem is all we know.
    public static string Humanize(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return processName;
        return char.ToUpper(processName[0], CultureInfo.CurrentCulture) + processName[1..];
    }

    private static string? TryFileDescription(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException or IOException)
        {
            return null;
        }
    }
}
