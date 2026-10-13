using Microsoft.UI.Windowing;

namespace Deckle.Shell;

// Single source of truth for the .ico paths the tray and every window use.
// Deckle.App.csproj copies them next to the exe, under Assets/Icons/.
public static class IconAssets
{
    private const string FileIdle      = "recording--indicator--false--32px.ico";
    private const string FileRecording = "recording--indicator--true--32px.ico";

    public static string? ResolvePath(bool recording)
    {
        string fileName = recording ? FileRecording : FileIdle;

        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    public static void ApplyToWindow(AppWindow appWindow, bool recording = false)
    {
        var path = ResolvePath(recording);
        if (path is not null) appWindow.SetIcon(path);
    }
}
