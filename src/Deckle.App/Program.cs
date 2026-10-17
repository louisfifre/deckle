using System.Diagnostics;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

// The entry point WinUI would otherwise generate (DISABLE_XAML_GENERATED_MAIN
// in the csproj), taken over for one reason: the crash dump capture has to be
// decided before the runtime hosts any XAML. See CrashDumpCapture for why the
// environment is the only lever.
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (TryRelaunchWithCrashDumpCapture(args)) return;

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    // True when this process handed over to a relaunched one and must exit.
    //
    // The relaunch goes through ShellExecute like every other Deckle restart,
    // so the child keeps the shell-launched foreground state the HUD's topmost
    // depends on. The resident mutex is not taken yet, so the child acquires
    // it as the first resident. The variables are set in this process's
    // environment, which the child inherits.
    //
    // Install, update and relocation modes are excluded: the process that
    // spawned them follows this very PID, and a hand-over would look like an
    // early exit.
    private static bool TryRelaunchWithCrashDumpCapture(string[] args)
    {
        if (App.IsTransactionalMode(args)) return false;
        if (CrashDumpCapture.Decide(Environment.GetEnvironmentVariable(CrashDumpCapture.EnableVariable))
            != CrashDumpCapture.Decision.ArmAndRelaunch)
            return false;

        string? exePath = Environment.ProcessPath;
        if (exePath is null) return false;

        try
        {
            Directory.CreateDirectory(AppPaths.CrashDumpsDirectory);
            foreach (var (name, value) in CrashDumpCapture.Variables(AppPaths.CrashDumpsDirectory))
                Environment.SetEnvironmentVariable(name, value);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            Process.Start(psi);
            return true;
        }
        catch
        {
            // No diagnostics sink exists yet. The boot goes on without capture
            // and the startup log says so (CrashDumpCaptureOff).
            foreach (var name in CrashDumpCapture.Variables(AppPaths.CrashDumpsDirectory).Keys)
                Environment.SetEnvironmentVariable(name, null);
            return false;
        }
    }
}
