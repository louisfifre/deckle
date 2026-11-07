using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    public bool Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return false;
            if (_started) return true;

            _host.KeyReceived += OnKey;
            _host.PointerInteraction += OnPointerInteraction;
            _host.FocusChanged += OnFocusChanged;
            _host.DrainRequested += OnDrainRequested;
            _tracker.WordCommitted += OnWordCommitted;
            _tracker.WordEdited += OnWordEdited;
            _tracker.TrackerReset += OnTrackerReset;

            if (!_host.Start())
            {
                Unsubscribe();
                DeckleAutocorrectSource.Log.EngineStartFailed(
                    DeckleAutocorrectSource.EngineStartFailureReasons.HostStartFailed);
                return false;
            }

            _started = true;
            OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, true);
            OnFocusChanged(); // seed the surface before the first focus event
            DeckleAutocorrectSource.Log.EngineStarted();
            return true;
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
            StopCore();
    }

    private void StopCore()
    {
        if (!_started) return;
        _started = false;
        _host.Stop();
        Unsubscribe();
        _corpus?.Discard();
        _stream?.Discard();
        _dictionary?.Flush();
        OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, false);
        DeckleAutocorrectSource.Log.EngineStopped();
    }

    // Permanent teardown. Stop alone is reused across enable/disable cycles.
    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
        }
    }

    private void Unsubscribe()
    {
        _host.KeyReceived -= OnKey;
        _host.PointerInteraction -= OnPointerInteraction;
        _host.FocusChanged -= OnFocusChanged;
        _host.DrainRequested -= OnDrainRequested;
        _tracker.WordCommitted -= OnWordCommitted;
        _tracker.WordEdited -= OnWordEdited;
        _tracker.TrackerReset -= OnTrackerReset;
    }
}
