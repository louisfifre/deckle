using System.Security;
using Microsoft.Win32;

namespace Deckle.Shell;

// Windows keeps two colour modes, set apart in Settings > Personalization >
// Colors: the app mode, which a window's own theme follows, and the Windows
// mode, which paints the taskbar and the Start menu. What Deckle shows on the
// taskbar follows the second: a person can run light apps on a dark taskbar.
public static class WindowsMode
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName      = "SystemUsesLightTheme";

    // Read each time it is asked: the value changes while Deckle runs, by hand
    // or on a schedule.
    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return IsLight(key?.GetValue(ValueName));
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A missing or foreign value reads as dark: the tile made for a dark
    // taskbar is pale and stays visible on a light one too.
    internal static bool IsLight(object? registryValue)
        => registryValue is int value && value != 0;

    // The name WM_SETTINGCHANGE carries when either colour mode changes.
    internal static bool IsModeChange(string? changedSetting)
        => changedSetting == "ImmersiveColorSet";
}
