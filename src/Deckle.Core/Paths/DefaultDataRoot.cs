using System;
using System.IO;

namespace Deckle.Core;

// The folder Deckle keeps its user data in when DECKLE_DATA_ROOT is unset:
// %LOCALAPPDATA%\Deckle, or a Deckle folder beside the running exe when
// LOCALAPPDATA is not available (sandboxed runs).
//
// This is the one place that default is written. The app reads it through
// AppPaths, and the installer, the setup wizard and the uninstaller read it
// through InstallPaths, so a reset, a relocation back to the default and an
// uninstall all agree on the same folder.
//
// It lives outside AppPaths on purpose. Touching AppPaths runs its static
// constructor, which creates the data folders and migrates old layouts. The
// uninstaller must be able to ask where the default folder is without
// recreating it, so this class has no static state and never creates a Deckle
// folder.
public static class DefaultDataRoot
{
    public static string Resolve()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        if (!string.IsNullOrWhiteSpace(localAppData))
            return Path.Combine(localAppData, AppPaths.AppFolderName);

        return Path.Combine(AppContext.BaseDirectory, AppPaths.AppFolderName);
    }
}
