using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Deckle.Atelier;

// Where darktable was found, so the log can say which way worked.
public enum DarktableSource
{
    AppPathsUser,
    AppPathsMachine,
    ProgramFiles,
    UserPrograms,
}

public sealed record DarktableInstall(string ExecutablePath, DarktableSource FoundBy);

public enum DarktableOpenResult
{
    Opened,
    // darktable is not installed, or not where its installer puts it; the
    // view says so (Open question 33, "darktable missing").
    NotFound,
    FolderMissing,
    Failed,
}

// Finds darktable and opens it on a folder, for "Ouvrir dans darktable" (12.4).
//
// Where it is looked for, in order:
// 1. The App Paths key, per user then per machine. darktable's Windows
//    installer writes it under HKA (HKCU or HKLM, by install mode): see
//    packaging/windows/darktable.iss.in in darktable's sources, whose comment
//    says registering there is "critical for GIMP to find darktable".
// 2. The installer's default folders: {autopf}\darktable\bin\darktable.exe,
//    that is %ProgramFiles% for a machine install and %LOCALAPPDATA%\Programs
//    for a per-user one (same file, DefaultDirName and the [Registry] lines).
//
// The command line is the executable and the folder alone: "If a folder is
// given darktable starts in lighttable view with the content of that folder
// as the current collection" (darktable manual, program invocation). That
// imports the folder into darktable's library, which is the hand-over: from
// then on darktable's database is what counts (spec, Limits).
public static class Darktable
{
    public const string ExecutableName = "darktable.exe";
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + ExecutableName;

    public static DarktableInstall? Find() => Find(ReadAppPath, File.Exists, Environment.GetFolderPath);

    internal static DarktableInstall? Find(Func<RegistryHive, string?> appPath, Func<string, bool> exists,
                                           Func<Environment.SpecialFolder, string> specialFolder)
    {
        foreach ((RegistryHive hive, DarktableSource source) in new[]
                 {
                     (RegistryHive.CurrentUser, DarktableSource.AppPathsUser),
                     (RegistryHive.LocalMachine, DarktableSource.AppPathsMachine),
                 })
        {
            string? path = Unquote(appPath(hive));
            if (path is not null && exists(path)) return new DarktableInstall(path, source);
        }

        foreach ((Environment.SpecialFolder folder, string relative, DarktableSource source) in new[]
                 {
                     (Environment.SpecialFolder.ProgramFiles, @"darktable\bin\" + ExecutableName, DarktableSource.ProgramFiles),
                     (Environment.SpecialFolder.LocalApplicationData, @"Programs\darktable\bin\" + ExecutableName, DarktableSource.UserPrograms),
                 })
        {
            string root = specialFolder(folder);
            if (string.IsNullOrEmpty(root)) continue;
            string path = Path.Combine(root, relative);
            if (exists(path)) return new DarktableInstall(path, source);
        }
        return null;
    }

    // The folder is passed as one argument, never concatenated into a string,
    // so a path with spaces or quotes reaches darktable intact.
    public static ProcessStartInfo OpenFolderCommand(DarktableInstall install, string folder)
    {
        var start = new ProcessStartInfo(install.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(install.ExecutablePath)!,
        };
        start.ArgumentList.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        return start;
    }

    public static DarktableOpenResult Open(string folder, DarktableInstall? install = null)
    {
        if (!Directory.Exists(folder)) return NotOpened(DarktableOpenResult.FolderMissing);
        install ??= Find();
        if (install is null) return NotOpened(DarktableOpenResult.NotFound);
        try
        {
            using Process? process = Process.Start(OpenFolderCommand(install, folder));
            DeckleAtelierSource.Log.DarktableOpened(install.FoundBy.ToString());
            return DarktableOpenResult.Opened;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return NotOpened(DarktableOpenResult.Failed);
        }
    }

    private static DarktableOpenResult NotOpened(DarktableOpenResult result)
    {
        DeckleAtelierSource.Log.DarktableNotOpened(result.ToString());
        return result;
    }

    private static string? ReadAppPath(RegistryHive hive)
    {
        try
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using RegistryKey? key = root.OpenSubKey(AppPathsKey);
            return key?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // App Paths values are sometimes written with quotes, and may hold
    // environment variables (REG_EXPAND_SZ).
    private static string? Unquote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string trimmed = Environment.ExpandEnvironmentVariables(value.Trim()).Trim('"');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
