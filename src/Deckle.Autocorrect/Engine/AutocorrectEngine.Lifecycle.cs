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
        _pauseTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _host.Stop();
        Unsubscribe();
        _corpus?.Discard();
        _stream?.Discard();
        _coordinator?.Invalidate(ResetReason.FocusChanged); // drop the sentence model
        _dictionary?.Flush();
        OperationalLogAdmission.SetActive(OperationalLogActivity.Autocorrect, false);
        DeckleAutocorrectSource.Log.EngineStopped();
    }

    // Permanent teardown: stop, then tear down the rerank lane (joins its worker
    // and releases the model). Stop alone is reused across enable/disable cycles.
    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
        }

        _pauseTimer?.Dispose();
        _coordinator?.Dispose();
        _lane?.Dispose();
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
