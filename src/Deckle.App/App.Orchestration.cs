using Deckle.Orchestration;

namespace Deckle.App;

public partial class App
{
    private OrchestrationRuntime? _orchestration;

    // The NullForegroundMonitor is the seam the shared foreground service
    // fills at integration: a small adapter here, from that service's own
    // event to IForegroundMonitor, so neither module references the other.
    private void InitializeOrchestration()
    {
        var runtime = new OrchestrationRuntime(
            OrchestrationSettingsService.Instance,
            new NullForegroundMonitor(),
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
        OrchestrationSettingsService.Instance.Flush();
    }
}
