using Deckle.App;
using Deckle.Lighting.Ambient;

namespace Deckle.App;

public partial class App
{
    private readonly SemaphoreSlim _ambientLock = new(1, 1);

    // The start-time settings the running engine started with; null while
    // the app has not started it. Read and written under _ambientLock.
    private AmbientStartSettings? _ambientStartedWith;

    private void OnAmbientSettingsChanged()
        => _ = ApplyAmbientSettingsAsync();

    // Starts or stops the engine on Enabled, and restarts a running engine
    // when the monitor, the pipeline shape or the capture rate changed:
    // the engine reads those three only at start.
    private async Task ApplyAmbientSettingsAsync()
    {
        if (_ambientEngine is null) return;

        await _ambientLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = AmbientSettingsService.Instance.Current;
            var startSettings = AmbientStartSettings.From(settings);

            if (settings.Enabled && _ambientEngine.IsRunning
                && _ambientStartedWith is { } startedWith)
            {
                string changed = startedWith.ChangedSettings(startSettings);
                if (changed.Length == 0) return;

                DeckleAmbientSource.Log.PipelineRestartDetail(changed);
                _ambientEngine.Stop();
                await StartAmbientEngineAsync(startSettings).ConfigureAwait(false);
            }
            else if (settings.Enabled && !_ambientEngine.IsRunning)
            {
                await StartAmbientEngineAsync(startSettings).ConfigureAwait(false);
            }
            else if (!settings.Enabled && _ambientEngine.IsRunning)
            {
                _ambientEngine.Stop();
                _ambientStartedWith = null;
            }
        }
        finally
        {
            _ambientLock.Release();
        }
    }

    // Records the start-time settings before StartAsync reads them: a change
    // landing during the start then shows as a difference on the next
    // Changed and restarts once more, rather than being missed.
    private async Task StartAmbientEngineAsync(AmbientStartSettings startSettings)
    {
        _ambientStartedWith = startSettings;
        try
        {
            await _ambientEngine!.StartAsync().ConfigureAwait(false);
        }
        catch
        {
            _ambientStartedWith = null;
            var s = AmbientSettingsService.Instance.Current;
            s.Enabled = false;
            AmbientSettingsService.Instance.Save();
        }
    }
}
