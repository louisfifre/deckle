using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Deckle.Folders;

// The shell's own ways in, the ones File Explorer uses.
//
// Open goes through ShellExecuteEx (Process.Start with UseShellExecute), so a
// file opens with the application Windows associates with its type. With
// ErrorDialog set, the shell keeps its own UI: a type with no application
// gets Windows' "How do you want to open this file?", as in File Explorer,
// rather than a silent failure. Given a folder, the shell opens it in File
// Explorer, which is how an added folder's menu shows it there.
//
// ShowInFolder goes through SHOpenFolderAndSelectItems: File Explorer opens
// the item's folder, or comes to a window already on it, with the item
// selected. Given one absolute item and no children, the call selects that
// item in its parent (Microsoft Learn, SHOpenFolderAndSelectItems, cidl). The
// shell wants COM in a single-threaded apartment, so the call runs on a
// thread of its own, started for it and joined before it returns, as
// ShellRecycleBin does. A command line ("explorer /select,path") would do
// the same but splits a path that holds a comma.
//
// Both block on the shell, so callers run them off the UI thread.
public sealed partial class ShellFileLauncher : IFileLauncher
{
    public LaunchOutcome Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return NotLaunched("open", path, "missing", 0, LaunchStatus.Missing);
        try
        {
            // The process, when the shell hands one back, is the
            // application's own: nothing here waits on it.
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                ErrorDialog = true,
            })?.Dispose();
            return Launched("open", path);
        }
        catch (Win32Exception ex)
        {
            return NotLaunched("open", path, "shell-error", ex.NativeErrorCode, LaunchStatus.Failed);
        }
        catch (InvalidOperationException ex)
        {
            return NotLaunched("open", path, "shell-error", ex.HResult, LaunchStatus.Failed);
        }
    }

    public LaunchOutcome ShowInFolder(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return NotLaunched("show-in-folder", path, "missing", 0, LaunchStatus.Missing);

        int hr = 0;
        var thread = new Thread(() => hr = SelectInFolder(path))
        {
            IsBackground = true,
            Name = "Folders show in folder",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return hr < 0
            ? NotLaunched("show-in-folder", path, "shell-error", hr, LaunchStatus.Failed)
            : Launched("show-in-folder", path);
    }

    private static int SelectInFolder(string path)
    {
        nint item = ILCreateFromPathW(path);
        if (item == 0) return E_FAIL;
        try
        {
            return SHOpenFolderAndSelectItems(item, 0, 0, 0);
        }
        finally
        {
            ILFree(item);
        }
    }

    private static LaunchOutcome Launched(string action, string path)
    {
        DeckleFoldersSource.Log.ItemLaunched(action, Extension(path));
        return new LaunchOutcome(LaunchStatus.Launched);
    }

    private static LaunchOutcome NotLaunched(string action, string path, string reason, int code, LaunchStatus status)
    {
        DeckleFoldersSource.Log.ItemNotLaunched(action, Extension(path), reason, code);
        return new LaunchOutcome(status, code);
    }

    private static string Extension(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant();

    private const int E_FAIL = unchecked((int)0x80004005);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint ILCreateFromPathW(string pszPath);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(nint pidl);

    [LibraryImport("shell32.dll")]
    private static partial int SHOpenFolderAndSelectItems(nint pidlFolder, uint cidl, nint apidl, uint dwFlags);
}
