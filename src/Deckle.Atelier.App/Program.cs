using Deckle.Core;

namespace Deckle.Atelier.App;

// The entry point WinUI would otherwise generate (DISABLE_XAML_GENERATED_MAIN
// in the csproj), taken over so the app's identity is declared before
// anything reads AppPaths: the data root, the log folder and the resident
// mutex all follow from it.
public static class Program
{
    public const string AppId = "atelier";

    [STAThread]
    static void Main()
    {
        AppIdentity.Use(AppIdentity.Create(AppId));

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
