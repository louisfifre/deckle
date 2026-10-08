using Deckle.Orchestration;

namespace Deckle.App;

public partial class App
{
    private OrchestrationRuntime? _orchestration;
    private WatcherForegroundMonitor? _orchestrationForeground;

    // The runtime reads foreground changes from the one ForegroundWatcher
    // started just before it (InitializeStartupModules), through an adapter
    // that subscribes to it: no second set of WinEvent hooks. An app launched
    // after Deckle is noticed when it comes to the front, elevated or not.
    private void InitializeOrchestration()
    {
        _orchestrationForeground = new WatcherForegroundMonitor(_foregroundWatcher!);
        var runtime = new OrchestrationRuntime(
            OrchestrationSettingsService.Instance,
            _orchestrationForeground,
            new WmiProcessStartSource(),
            new ProcessExitWatch(),
            effects: [new HdrRuleEffect()]);
        _orchestration = runtime;

        // The settings page reads the detection status through this hook
        // rather than a reference to App.
        OrchestrationRuntime.StatusProvider = () => runtime.Status;
        runtime.Start();
    }

    // Disposing ends every session first, so whatever a session applied is
    // restored before Deckle exits.
    private void ShutdownOrchestration()
    {
        OrchestrationRuntime.StatusProvider = null;
        _orchestration?.Dispose();
        _orchestration = null;
        _orchestrationForeground?.Dispose();
        _orchestrationForeground = null;
        OrchestrationSettingsService.Instance.Flush();
    }
}
