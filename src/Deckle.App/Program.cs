using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

// The entry point WinUI would otherwise generate (DISABLE_XAML_GENERATED_MAIN
// in the csproj), taken over for one reason: crash recovery has to be decided
// before the runtime hosts any XAML. The process the shell starts supervises
// the real Deckle (see CrashSupervisor), which also arms the crash dump capture
// (see CrashDumpCapture for why the environment is the only lever).
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (CrashSupervisor.TryRun(args)) return;

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
