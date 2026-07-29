using Deckle.Input.PrecisionScroll;

namespace Deckle.App;

public partial class App
{
    private PrecisionScrollEngine? _precisionScrollEngine;
    private bool _precisionScrollRunning;

    private void InitializePrecisionScroll()
    {
        if (_keyboardMouseHost is null) return;

        PrecisionScrollSettingsService.Instance.Changed += ReconcilePrecisionScroll;
        ReconcilePrecisionScroll();
    }

    // Settings notifications are synchronous with the in-memory mutation;
    // persistence remains debounced in the owning module.
    private void ReconcilePrecisionScroll()
    {
        if (_keyboardMouseHost is null) return;

        PrecisionScrollSettings settings = PrecisionScrollSettingsService.Instance.Current;

        if (settings.Enabled && !_precisionScrollRunning)
        {
            _precisionScrollEngine = new PrecisionScrollEngine(_keyboardMouseHost);
            _precisionScrollEngine.SetTuning(settings.Tuning ?? new PrecisionScrollTuning());
            if (_keyboardMouseHost.Start())
            {
                _precisionScrollRunning = _precisionScrollEngine.Start();
                if (!_precisionScrollRunning)
                {
                    _keyboardMouseHost.Stop();
                    _precisionScrollEngine.Dispose();
                    _precisionScrollEngine = null;
                }
            }
            else
            {
                _precisionScrollEngine.Dispose();
                _precisionScrollEngine = null;
            }
        }
        else if (settings.Enabled)
        {
            _precisionScrollEngine?.SetTuning(settings.Tuning ?? new PrecisionScrollTuning());
        }
        else if (!settings.Enabled && _precisionScrollRunning)
        {
            _precisionScrollEngine?.Dispose();
            _precisionScrollEngine = null;
            _keyboardMouseHost.Stop();
            _precisionScrollRunning = false;
        }
    }

    private void ShutdownPrecisionScroll()
    {
        PrecisionScrollSettingsService.Instance.Changed -= ReconcilePrecisionScroll;
        _precisionScrollEngine?.Dispose();
        _precisionScrollEngine = null;
        if (_precisionScrollRunning)
            _keyboardMouseHost?.Stop();
        _precisionScrollRunning = false;
        PrecisionScrollSettingsService.Instance.Flush();
    }
}
