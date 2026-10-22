using Deckle.Input.PrecisionScroll;

namespace Deckle.App;

public partial class App
{
    // Owns the engine and the share of the input host it uses, and restarts the
    // worker once when it ends on its own (see PrecisionScrollRuntime). Null until
    // the module is composed with the shared host.
    private PrecisionScrollRuntime? _precisionScrollRuntime;

    // Set when the module is composed. Shutdown must not reach for
    // PrecisionScrollSettingsService.Instance on a boot that left the module
    // out: its constructor creates the module's settings store, which an absent
    // module must not leave behind. Not ModulePresence.IsPresent, because a
    // presence change is adopted in place before the restart that applies it.
    private bool _precisionScrollComposed;

    private void InitializePrecisionScroll()
    {
        _precisionScrollComposed = true;
        if (_keyboardMouseHost is null) return;

        // While the switch is on but nothing runs, every settings change retries
        // the start; the runtime names the failure once until a start succeeds.
        _precisionScrollRuntime = new PrecisionScrollRuntime(
            _keyboardMouseHost,
            reason => DeckleAppSource.Log.InputConsumerUnavailable("precision-scroll", reason));
        PrecisionScrollSettingsService.Instance.Changed += ReconcilePrecisionScroll;
        ReconcilePrecisionScroll();
    }

    // Settings notifications are synchronous with the in-memory mutation;
    // persistence remains debounced in the owning module.
    private void ReconcilePrecisionScroll()
    {
        PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;
        _precisionScrollRuntime?.Apply(settings.Enabled, settings.Tuning);
    }

    private void ShutdownPrecisionScroll()
    {
        if (!_precisionScrollComposed) return;

        PrecisionScrollSettingsService.Instance.Changed -= ReconcilePrecisionScroll;
        _precisionScrollRuntime?.Dispose();
        _precisionScrollRuntime = null;
        PrecisionScrollSettingsService.Instance.Flush();
    }
}
