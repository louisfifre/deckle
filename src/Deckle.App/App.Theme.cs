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
                 { app._settingsWindow, app._playgroundWindow, app._logWindow, app._hudWindow, app._transcriptPanel })
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
