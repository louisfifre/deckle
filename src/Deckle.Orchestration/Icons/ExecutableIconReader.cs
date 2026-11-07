using System.Runtime.InteropServices;

namespace Deckle.Orchestration;

/// <summary>
/// Reads the icon the shell shows for an executable. It never invents one: a
/// file that cannot give its icon gives nothing, and the page then shows no
/// icon at all.
/// </summary>
public static class ExecutableIconReader
{
    // The shell renders at the size asked; the header shows it at about half,
    // so a high-DPI screen still has pixels to spare.
    public const int IconSize = 32;

    private const int HrFileNotFound = unchecked((int)0x80070002);
    private const int HrPathNotFound = unchecked((int)0x80070003);
    private const int HrAccessDenied = unchecked((int)0x80070005);
    private const int HrInvalidName = unchecked((int)0x8007007B);

    /// <summary>
    /// The icon of the file, or null when the file is not there or not readable
    /// (a moved game, an unplugged drive, a protected folder). Those are
    /// expected and silent; any other failure throws so the caller can log it.
    /// </summary>
    public static ExecutableIcon? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            return ShellIconNativeMethods.GetIcon(path, IconSize);
        }
        catch (COMException ex) when (ex.HResult is HrFileNotFound or HrPathNotFound or HrAccessDenied or HrInvalidName)
        {
            return null;
        }
    }
}
