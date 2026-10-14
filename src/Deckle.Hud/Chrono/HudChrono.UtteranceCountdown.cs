using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Deckle.Composition;

namespace Deckle.Hud;

public sealed partial class HudChrono
{
    // Capture only writes this immutable mailbox. All visual work stays on the
    // UI rendering loop, including resource disposal.
    private sealed record CountdownSample(bool Active, float Progress, int Completed);
    private CountdownSample? _latestCountdown;
    private CountdownSample? _consumedCountdown;
    private readonly UtteranceCountdownState _countdownState = new();
    private HudComposition.UtteranceCountdownVisual? _countdownVisual;
    private bool _countdownListening;
    private float _renderedCountdownValue = -1f;
    private long _countdownStarted;

    public void UpdateUtteranceProgress(bool active, float progress, int completed)
    {
        if (!Volatile.Read(ref _countdownListening)) return;
        Volatile.Write(ref _latestCountdown, new CountdownSample(active, progress, completed));
    }

    private void SetCountdownRecording(bool recording)
    {
        if (recording == Volatile.Read(ref _countdownListening)) return;
        Volatile.Write(ref _countdownListening, recording);
        Volatile.Write(ref _latestCountdown, null);
        _consumedCountdown = null;
        _countdownState.Reset();
        _countdownStarted = Stopwatch.GetTimestamp();
        DetachCountdownVisual();
    }

    private void RenderUtteranceCountdown()
    {
        if (!_countdownListening) return;
        var sample = Volatile.Read(ref _latestCountdown);
        if (sample is null) return;
        if (!sample.Active)
        {
            _countdownState.Reset();
            _consumedCountdown = sample;
            DetachCountdownVisual();
            return;
        }

        var now = Stopwatch.GetElapsedTime(_countdownStarted);
        if (!ReferenceEquals(sample, _consumedCountdown))
        {
            _countdownState.Accept(sample.Progress, sample.Completed, now);
            _consumedCountdown = sample;
        }
        if (_countdownVisual is null) RebuildCountdownVisual();
        float value = _countdownState.ValueAt(now);
        if (value == _renderedCountdownValue || _countdownVisual is null) return;
        _countdownVisual.SetProgress(value, interpolate: _animationsEnabled);
        _renderedCountdownValue = value;
    }

    private void RebuildCountdownVisual()
    {
        DetachCountdownVisual();
        if (!_countdownListening || Volatile.Read(ref _latestCountdown) is not { Active: true }) return;
        // Wait for actual layout: this also avoids rebuilding a fallback-size
        // visual when the window is first shown.
        float width = (float)CountdownSurfaceHost.ActualWidth;
        float height = (float)CountdownSurfaceHost.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var size = new Vector2(width, height);
        var compositor = ElementCompositionPreview.GetElementVisual(CountdownSurfaceHost).Compositor;
        // The stored settings over the compiled defaults. The Playground tunes
        // its own preview and reaches this visual only through its Save, which
        // lands here via OnHudSettingsChanged.
        var settings = HudSettingsService.Instance;
        _countdownVisual = HudComposition.CreateUtteranceCountdownVisual(
            compositor, size, size, settings.EffectiveStroke(),
            ChronoRoot.ActualTheme == ElementTheme.Dark,
            settings.EffectiveCountdown(), materialOpacity: 0f, animationsEnabled: _animationsEnabled);
        _renderedCountdownValue = _countdownState.ValueAt(Stopwatch.GetElapsedTime(_countdownStarted));
        _countdownVisual.SetProgress(_renderedCountdownValue);
        ElementCompositionPreview.SetElementChildVisual(CountdownSurfaceHost, _countdownVisual.Container);
    }

    private void DetachCountdownVisual()
    {
        if (_countdownVisual is null) return;
        ElementCompositionPreview.SetElementChildVisual(CountdownSurfaceHost, null);
        _countdownVisual.Dispose();
        _countdownVisual = null;
        _renderedCountdownValue = -1f;
    }
}
