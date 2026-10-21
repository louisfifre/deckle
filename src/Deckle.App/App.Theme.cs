namespace Deckle.App;

public partial class App
{
    // Lazy windows read these at creation.
    private static Microsoft.UI.Xaml.ElementTheme _currentTheme =
        Microsoft.UI.Xaml.ElementTheme.Default;
    private static Microsoft.UI.Windowing.TitleBarTheme _currentTitleBarTheme =
        Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode;

    private static bool _firstThemeApplyDone;

    // Entry point for the boot and the engine's auto-calibration.
    public static void ApplyLevelWindow(Audio.LevelWindowSettings cfg)
        => Audio.AudioLevelMapper.Apply(cfg);

    public static void ApplyTheme(string themeName)
    {
        var theme = themeName switch
        {
            "Light" => Microsoft.UI.Xaml.ElementTheme.Light,
            "Dark"  => Microsoft.UI.Xaml.ElementTheme.Dark,
            _       => Microsoft.UI.Xaml.ElementTheme.Default,
        };

        // "System" is the only other name AppSettings writes; anything else is
        // a hand-edited or corrupted settings file that silently falls back.
        if (themeName is not ("Light" or "Dark" or "System"))
            DeckleAppSource.Log.ThemeUnknown(themeName ?? "null");

        var titleBarTheme = theme switch
        {
            Microsoft.UI.Xaml.ElementTheme.Light => Microsoft.UI.Windowing.TitleBarTheme.Light,
            Microsoft.UI.Xaml.ElementTheme.Dark  => Microsoft.UI.Windowing.TitleBarTheme.Dark,
            _                                     => Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode,
        };

        _currentTheme = theme;
        _currentTitleBarTheme = titleBarTheme;

        if (Current is not App app) return;

        string source = _firstThemeApplyDone ? "user" : "app-init";
        _firstThemeApplyDone = true;

        int windows = 0;
        foreach (var window in new Microsoft.UI.Xaml.Window?[]
                 { app._settingsWindow, app._playgroundWindow, app._logWindow, app._hudWindow, app._transcriptPanel })
        {
            if (window is null) continue;
            ApplyThemeToSingle(window, source);
            windows++;
        }

        DeckleAppSource.Log.ThemeApplied(theme.ToString(), source, windows);
    }

    // A lazily created window takes the current theme as its initial one,
    // hence the app-init default. ApplyTheme passes its own source: pushing
    // app-init here unconditionally used to relabel every user switch.
    private static void ApplyThemeToSingle(Microsoft.UI.Xaml.Window? window, string source = "app-init")
    {
        if (window is null) return;
        Deckle.Diagnostics.ThemeRequestSourceProbe.Push(source);
        if (window.Content is Microsoft.UI.Xaml.FrameworkElement fe)
            fe.RequestedTheme = _currentTheme;
        if (window.AppWindow?.TitleBar is { } tb)
            tb.PreferredTheme = _currentTitleBarTheme;
    }
}
