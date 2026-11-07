using System.Runtime.CompilerServices;
using Deckle.Core;

namespace Deckle.Shell.Taskbar.Tests;

// Points the whole test process at an empty data root before anything reads
// AppPaths, through the override the app itself honours (DECKLE_DATA_ROOT), so
// the settings service the page's ViewModel loads opens a fresh profile in a
// temporary folder and never the maintainer's real modules/taskbar/settings.json.
// The variable is set on this process only; the folder is deleted at exit.
internal static class IsolatedDataRoot
{
    public static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "Deckle.Shell.Taskbar.Tests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataRootEnvVar, Path);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
}
