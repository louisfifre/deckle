using Microsoft.UI.Windowing;

namespace Deckle.Shell;

// Single source of truth for the icon paths the tray and every window use.
// Deckle.App.csproj copies them next to the exe, under Assets/Icons/.
public static class IconAssets
{
    private const string FileIdle      = "recording--indicator--false--32px.ico";
    private const string FileRecording = "recording--indicator--true--32px.ico";

    // The same mark as a single 32 px bitmap, for a XAML image. A BitmapImage
    // of the .ico decodes only its first frame, the 16 px one, which a scaled
    // title bar then stretches into a blur; the .ico stays the window icon,
    // where the shell picks the frame it needs.
    private const string ImageIdle      = "recording--indicator--false--32px.png";
    private const string ImageRecording = "recording--indicator--true--32px.png";

    // The tray's marks while a user incident is open: the same states with a
    // dot. Provisional copies of the marks above until the maintainer draws
    // them; a 16 px file sits beside each for when the tray loads that size.
    private const string FileIdleIncident      = "recording--indicator--false--incident--true--32px.ico";
    private const string FileRecordingIncident = "recording--indicator--true--incident--true--32px.ico";

    public static string? ResolvePath(bool recording)
        => Resolve(recording ? FileRecording : FileIdle);

    public static string? ResolveTrayIconPath(bool recording, bool incident)
        => incident
            ? Resolve(recording ? FileRecordingIncident : FileIdleIncident)
            : ResolvePath(recording);

    public static string? ResolveImagePath(bool recording)
        => Resolve(recording ? ImageRecording : ImageIdle);

    public static void ApplyToWindow(AppWindow appWindow, bool recording = false)
    {
        var path = ResolvePath(recording);
        if (path is not null) appWindow.SetIcon(path);
    }

    private static string? Resolve(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", fileName);
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }
}
