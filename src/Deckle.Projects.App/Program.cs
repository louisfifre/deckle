using Deckle.Core;

namespace Deckle.Projects.App;

// The entry point WinUI would otherwise generate (DISABLE_XAML_GENERATED_MAIN
// in the csproj), taken over so the app's identity is declared before
// anything reads AppPaths: the data root, the log folder and the resident
// mutex all follow from it.
public static class Program
{
    public const string AppId = "projects";

    // What the one-time copy of the published Deckle's Projects data did, for
    // the app to log once its log files are open (nothing can be logged before
    // that).
    public static ProjectsDataCopyResult DataCopy { get; private set; } =
        new(ProjectsDataCopyOutcome.NothingToCopy, null);

    [STAThread]
    static void Main()
    {
        AppIdentity.Use(AppIdentity.Create(AppId));

        // Before anything reads AppPaths, which creates the app's data root.
        // Never in the self-check: its run works on a root of its own, and a
        // run refused for want of one must not touch the maintainer's.
        if (!App.IsSelfCheckMode(Environment.GetCommandLineArgs()))
        {
            string? rootOverride = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvVar);
            string suiteRoot = string.IsNullOrWhiteSpace(rootOverride)
                ? DefaultDataRoot.Resolve()
                : Path.GetFullPath(rootOverride);
            DataCopy = ProjectsDataCopy.Run(suiteRoot);
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
