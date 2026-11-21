using Deckle.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Deckle.Hosting;

// The theme every window of the process takes: one current choice, applied to
// a window's content and title bar. An app with a Settings page sets it from
// the stored choice; an app without follows the system, which is the default.
public static class WindowTheme
{
    // A lazily created window reads these at creation.
    public static ElementTheme Current { get; private set; } = ElementTheme.Default;
    public static TitleBarTheme TitleBar { get; private set; } = TitleBarTheme.UseDefaultAppMode;

    // The choice the stored theme name stands for: "Light", "Dark", anything
    // else follows the system. Returns it without applying it, so the caller
    // can log an unknown name and then push the theme to its open windows.
    public static ElementTheme FromName(string? themeName) => themeName switch
    {
        "Light" => ElementTheme.Light,
        "Dark"  => ElementTheme.Dark,
        _       => ElementTheme.Default,
    };

    public static void Set(ElementTheme theme)
    {
        Current = theme;
        TitleBar = theme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark  => TitleBarTheme.Dark,
            _                  => TitleBarTheme.UseDefaultAppMode,
        };
    }

    // A lazily created window takes the current theme as its initial one,
    // hence the app-init default. A user switch passes its own source: pushing
    // app-init unconditionally used to relabel every user switch.
    public static void Apply(Window? window, string source = "app-init")
    {
        if (window is null) return;
        ThemeRequestSourceProbe.Push(source);
        if (window.Content is FrameworkElement fe)
            fe.RequestedTheme = Current;
        if (window.AppWindow?.TitleBar is { } tb)
            tb.PreferredTheme = TitleBar;
    }
}
