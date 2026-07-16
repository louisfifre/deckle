using Deckle.App;
using Deckle.Lighting.Ambient;

namespace Deckle.App;

public partial class App
{
    private readonly SemaphoreSlim _ambientLock = new(1, 1);

    private void OnAmbientSettingsChanged()
    {
        bool enabled = AmbientSettingsService.Instance.Current.Enabled;
        _ = ApplyAmbientEnabledAsync(enabled);
    }

    private async Task ApplyAmbientEnabledAsync(bool enabled)
    {
        if (_ambientEngine is null) return;

        await _ambientLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (enabled && !_ambientEngine.IsRunning)
            {
                try
                {
                    await _ambientEngine.StartAsync().ConfigureAwait(false);
                }
                catch
                {
                    var s = AmbientSettingsService.Instance.Current;
                    s.Enabled = false;
                    AmbientSettingsService.Instance.Save();
                }
            }
            else if (!enabled && _ambientEngine.IsRunning)
            {
                _ambientEngine.Stop();
            }
        }
        finally
        {
            _ambientLock.Release();
        }
    }
}
