using Microsoft.Win32;

namespace Deckle.Shell.Taskbar;

/// <summary>
/// A registry key a policy lives under: its hive and its path inside that hive.
/// The policy engine reads and writes through it, so the tests hand it a
/// temporary tree under HKCU\Software\Deckle.Tests instead of the real policy
/// keys.
/// </summary>
public sealed record PolicyKey(RegistryHive Hive, string Path)
{
    /// <summary>
    /// The full key name reg.exe takes: "HKCU\..." or "HKLM\...". Only the two
    /// hives a policy can live in are spelled.
    /// </summary>
    public string CommandLineName => Hive switch
    {
        RegistryHive.CurrentUser  => @"HKCU\" + Path,
        RegistryHive.LocalMachine => @"HKLM\" + Path,
        _ => throw new NotSupportedException($"A policy does not live in {Hive}."),
    };

    /// <summary>The DWORD the value holds, or null when the key or the value is absent, or not a DWORD.</summary>
    public uint? ReadDword(string valueName)
    {
        using RegistryKey hive = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);
        using RegistryKey? key = hive.OpenSubKey(Path);
        return key?.GetValue(valueName) is int value ? unchecked((uint)value) : null;
    }

    /// <summary>Creates the key if needed and writes the DWORD. Throws when the process may not write it.</summary>
    public void WriteDword(string valueName, uint value)
    {
        using RegistryKey hive = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);
        using RegistryKey key = hive.CreateSubKey(Path, writable: true);
        key.SetValue(valueName, unchecked((int)value), RegistryValueKind.DWord);
    }

    /// <summary>Deletes the value; an absent key or value is not an error. Throws when the process may not write it.</summary>
    public void DeleteValue(string valueName)
    {
        using RegistryKey hive = RegistryKey.OpenBaseKey(Hive, RegistryView.Default);
        using RegistryKey? key = hive.OpenSubKey(Path, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
