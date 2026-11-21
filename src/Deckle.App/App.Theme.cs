using Deckle.Hosting;

namespace Deckle.App;

public partial class App
{
    private static bool _firstThemeApplyDone;

    // Anchors the HUD outline on the stored voice level: at boot, then on
    // every change of the audio settings.
    private static void ApplyVoiceLevel()
        => Audio.AudioLevelMapper.Apply(Audio.CaptureSettingsService.Instance.Current.VoiceLevelDbfs);

    public static void ApplyTheme(string themeName)
    {
        var theme = WindowTheme.FromName(themeName);

        // "System" is the only other name AppSettings writes; anything else is
        // a hand-edited or corrupted settings file that silently falls back.
        if (themeName is not ("Light" or "Dark" or "System"))
            DeckleAppSource.Log.ThemeUnknown(themeName ?? "null");

        WindowTheme.Set(theme);

        if (Current is not App app) return;

        string source = _firstThemeApplyDone ? "user" : "app-init";
        _firstThemeApplyDone = true;

        int windows = 0;
        foreach (var window in new Microsoft.UI.Xaml.Window?[]
                 { app._settingsWindow, app._logWindow, app._hudWindow, app._transcriptPanel })
        {
            if (window is null) continue;
            WindowTheme.Apply(window, source);
            windows++;
        }
        foreach (Microsoft.UI.Xaml.Window window in app._publishedHost?.OpenWindows ?? [])
        {
            WindowTheme.Apply(window, source);
            windows++;
        }

        DeckleAppSource.Log.ThemeApplied(theme.ToString(), source, windows);
    }
}
