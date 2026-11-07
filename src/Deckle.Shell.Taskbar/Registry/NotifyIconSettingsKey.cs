using Microsoft.Win32;

namespace Deckle.Shell.Taskbar;

// Reads and writes the IsPromoted value of every icon under the notify icon
// settings key, by subkey name. Explorer keeps one subkey per icon it has ever
// shown; their other values (ExecutablePath, Publisher, IconSnapshot...) are
// the user's data and are never read here.
//
// The root is a constructor argument, the key this type governs: HKCU\Control
// Panel\NotifyIconSettings in the app, a temporary tree under HKCU\Software\
// Deckle.Tests in the tests.
internal sealed class NotifyIconSettingsKey(string rootPath, bool nudgeExplorer)
{
    // The temporary REG_SZ value the nudge writes then deletes. Its name only
    // has to be one explorer never uses.
    private const string NudgeValueName = "DeckleNudge";

    public string RootPath { get; } = rootPath;

    /// <summary>
    /// Every subkey's IsPromoted, null when the value is absent or is not a
    /// DWORD; null for the whole map when the root key does not exist.
    /// </summary>
    public IReadOnlyDictionary<string, uint?>? ReadPromotions()
    {
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(RootPath);
        if (root is null) return null;

        var promotions = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? icon = root.OpenSubKey(name);
            // Deleted between the listing and the open: Windows dropped it.
            if (icon is null) continue;
            promotions[name] = icon.GetValue(TrayIconPromotion.ValueName) is int value ? unchecked((uint)value) : null;
        }
        return promotions;
    }

    /// <summary>The full HKCU-relative path of an icon's subkey.</summary>
    public string PathOf(string subkey) => RootPath + @"\" + subkey;

    /// <summary>
    /// The subkey a full path names when it sits directly under the root, or
    /// null for any other path.
    /// </summary>
    public string? SubkeyOf(string keyPath)
    {
        string prefix = RootPath + @"\";
        if (!keyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string subkey = keyPath[prefix.Length..];
        return subkey.Length == 0 || subkey.Contains('\\') ? null : subkey;
    }

    /// <summary>
    /// Writes an icon's IsPromoted, or deletes it when <paramref name="value"/>
    /// is null. Returns false when the subkey no longer exists.
    /// </summary>
    public bool Write(string subkey, uint? value)
    {
        using RegistryKey? icon = Registry.CurrentUser.OpenSubKey(PathOf(subkey), writable: true);
        if (icon is null) return false;

        if (value is uint dword) icon.SetValue(TrayIconPromotion.ValueName, unchecked((int)dword), RegistryValueKind.DWord);
        else icon.DeleteValue(TrayIconPromotion.ValueName, throwOnMissingValue: false);

        if (nudgeExplorer) Nudge(icon);
        return true;
    }

    // Established 2026-10-08: writing IsPromoted alone was not seen applying
    // live on Louis's machine. The Windhawk mod taskbar-notification-icons-
    // show-all triggers explorer by writing then deleting a temporary REG_SZ
    // value on each subkey, because explorer keeps a registry watcher for each
    // notify icon item and re-reads the item when its key changes. The nudge
    // does the same after each IsPromoted write. It is on by default and
    // DECKLE_TASKBAR_TRAY_NUDGE=0 turns it off (TrayIconPromoter), so whether
    // it is needed can be checked on the machine with and without it.
    //
    // It cannot loop: the nudge only follows a write, the watcher's next pass
    // finds the value at 1 and writes nothing, so nothing is nudged again.
    private static void Nudge(RegistryKey icon)
    {
        icon.SetValue(NudgeValueName, string.Empty, RegistryValueKind.String);
        icon.DeleteValue(NudgeValueName, throwOnMissingValue: false);
    }
}
