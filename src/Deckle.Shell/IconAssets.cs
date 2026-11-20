using Microsoft.UI.Windowing;

namespace Deckle.Shell;

// What the mark shows. At rest its blades share one gradient; a blade lights
// up while a capability is at work: the large back blade while the microphone
// records, the flat right blade with it while a profile rewrites the
// transcript.
public enum MarkState { Rest, Recording, Rewriting }

// Single source of truth for the icon paths the tray and every window use.
// Deckle.App.csproj copies them next to the exe, under Assets/Icons/. The
// files are drawn by logo/generate.mjs, never by hand.
//
// The mark is bare, as Windows 11 icons are: the shape is the icon, with no
// tile behind it. Every file here carries the same four-blade form, from the
// tray to the taskbar.
public static class IconAssets
{
    // The tray's .ico of a state, with the dot it shows while a user incident
    // is open.
    private static string TrayIconFile(MarkState state, bool incident)
        => $"deckle--state--{Name(state)}{(incident ? "--incident--true" : "")}.ico";

    // A window's .ico: the tray's, without the incident dot. A window shows
    // the microphone, not the rewrite.
    internal static string WindowIconFile(bool recording)
        => TrayIconFile(recording ? MarkState.Recording : MarkState.Rest, incident: false);

    // The bare mark as a single 32 px bitmap, for a XAML image. A BitmapImage
    // of an .ico decodes only its first frame, the 16 px one, which a scaled
    // title bar then stretches into a blur.
    private static string ImageFile(MarkState state)
        => $"deckle--state--{Name(state)}--32px.png";

    private static string Name(MarkState state) => state switch
    {
        MarkState.Recording => "recording",
        MarkState.Rewriting => "rewriting",
        _                   => "rest",
    };

    public static string? ResolvePath(bool recording)
        => Resolve(WindowIconFile(recording));

    public static string? ResolveTrayIconPath(MarkState state, bool incident)
        => Resolve(TrayIconFile(state, incident));

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
