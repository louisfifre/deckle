using Deckle.Core;

namespace Deckle.Folders.App;

// The entry point WinUI would otherwise generate (DISABLE_XAML_GENERATED_MAIN
// in the csproj), taken over so the app's identity is declared before
// anything reads AppPaths: the data root, the log folder and the resident
// mutex all follow from it.
public static class Program
{
    public const string AppId = "folders";

    // What the one-time move of the old data folder did, for the app to log
    // once its log files are open (nothing can be logged before that).
    public static FoldersDataMigrationResult DataMigration { get; private set; } =
        new(FoldersDataMigrationOutcome.NothingToMove, null);

    [STAThread]
    static void Main()
    {
        AppIdentity.Use(AppIdentity.Create(AppId));

        // Before anything reads AppPaths, which creates the new data root. The
        // self-check runs on a temporary root of its own and never migrates.
        if (!App.IsSelfCheckMode(Environment.GetCommandLineArgs()))
        {
            string? rootOverride = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvVar);
            string suiteRoot = string.IsNullOrWhiteSpace(rootOverride)
                ? DefaultDataRoot.Resolve()
                : Path.GetFullPath(rootOverride);
            DataMigration = FoldersDataMigration.Run(suiteRoot);
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
