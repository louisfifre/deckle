using Microsoft.Win32;

namespace Deckle.Installer;

// ── UninstallEntry ────────────────────────────────────────────────────────────
//
// The "Installed apps" / Add-Remove-Programs registration, written under HKCU so
// it needs no admin — the per-user counterpart of the per-user install folder.
// The UninstallString points back at the installer copied into the install folder,
// re-invoked with --uninstall, so the same exe is both installer and uninstaller.
//
// Windows reads this key to list the app, show its version, and run its removal;
// NoModify/NoRepair hide the buttons we don't implement. The installer reads the
// same key back to recognise an existing copy and run as an update.
internal static class UninstallEntry
{
    private const string KeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Deckle";

    // What a previous run registered — null when Deckle was never installed (or
    // was uninstalled, which deletes the key).
    public sealed record ExistingInstall(string InstallDir, string Version);

    public static ExistingInstall? Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key?.GetValue("InstallLocation") is not string dir || string.IsNullOrWhiteSpace(dir)) return null;
        if (key.GetValue("DisplayVersion") is not string version || string.IsNullOrWhiteSpace(version)) return null;
        return new ExistingInstall(dir, version);
    }

    public static void Write(string installDir, string version, string uninstallerPath, long estimatedSizeBytes)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue("DisplayName", "Deckle");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "Louis Fifre");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", Path.Combine(installDir, "Deckle.exe"));
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall -y");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        // EstimatedSize is in KB and drives the size column in Installed apps.
        key.SetValue("EstimatedSize", (int)(estimatedSizeBytes / 1024), RegistryValueKind.DWord);
    }

    public static void Remove() =>
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
