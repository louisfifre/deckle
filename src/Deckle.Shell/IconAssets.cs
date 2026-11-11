using Microsoft.UI.Windowing;

namespace Deckle.Shell;

// What the mark shows. At rest its five blades share one gradient; a blade
// lights up while a capability is at work: the large back blade while the
// microphone records, the flat right blade with it while a profile rewrites
// the transcript.
public enum MarkState { Rest, Recording, Rewriting }

// Single source of truth for the icon paths the tray and every window use.
// Deckle.App.csproj copies them next to the exe, under Assets/Icons/. The
// files are drawn by logo/generate.mjs, never by hand.
public static class IconAssets
{
    // The .ico of a state, with the dot the tray shows while a user incident
    // is open.
    private static string IconFile(MarkState state, bool incident)
        => $"deckle--state--{Name(state)}{(incident ? "--incident--true" : "")}.ico";

    // The same mark as a single 32 px bitmap, for a XAML image. A BitmapImage
    // of the .ico decodes only its first frame, the 16 px one, which a scaled
    // title bar then stretches into a blur; the .ico stays the window icon,
    // where the shell picks the frame it needs.
    private static string ImageFile(MarkState state)
        => $"deckle--state--{Name(state)}--32px.png";

    private static string Name(MarkState state) => state switch
    {
        MarkState.Recording => "recording",
        MarkState.Rewriting => "rewriting",
        _                   => "rest",
    };

    public static string? ResolvePath(bool recording)
        => Resolve(IconFile(recording ? MarkState.Recording : MarkState.Rest, incident: false));

    public static string? ResolveTrayIconPath(MarkState state, bool incident)
        => Resolve(IconFile(state, incident));

    public static string? ResolveImagePath(bool recording)
        => Resolve(ImageFile(recording ? MarkState.Recording : MarkState.Rest));

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
