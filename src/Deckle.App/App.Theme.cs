namespace Deckle.App;

public partial class App
{
    // Current theme + caption button theme, kept in sync by ApplyTheme.
    // Lazy windows read these to apply the right palette at creation.
    private static Microsoft.UI.Xaml.ElementTheme _currentTheme =
        Microsoft.UI.Xaml.ElementTheme.Default;
    private static Microsoft.UI.Windowing.TitleBarTheme _currentTitleBarTheme =
        Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode;

    // Distinguishes the boot ApplyTheme call from user-triggered changes for
    // ThemeRequestSourceProbe labelling.
    private static bool _firstThemeApplyDone;

    // Boot-time and engine-side (auto-calibration) entry point for pushing the
    // level window into AudioLevelMapper. The Recording settings sliders no longer
    // go through here — they call AudioLevelMapper.Apply directly now that the page
    // lives in Deckle.Audio, so the former SettingsHost.ApplyLevelWindow shell hop
    // is gone.
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
        Deckle.Diagnostics.ThemeRequestSourceProbe.Push(source);
        _firstThemeApplyDone = true;

        foreach (var window in new Microsoft.UI.Xaml.Window?[]
                 { app._settingsWindow, app._playgroundWindow, app._logWindow, app._hudWindow })
        {
            ApplyThemeToSingle(window);
        }
    }

    private static void ApplyThemeToSingle(Microsoft.UI.Xaml.Window? window)
    {
        if (window is null) return;
        Deckle.Diagnostics.ThemeRequestSourceProbe.Push("app-init");
        if (window.Content is Microsoft.UI.Xaml.FrameworkElement fe)
            fe.RequestedTheme = _currentTheme;
        if (window.AppWindow?.TitleBar is { } tb)
            tb.PreferredTheme = _currentTitleBarTheme;
    }
}
