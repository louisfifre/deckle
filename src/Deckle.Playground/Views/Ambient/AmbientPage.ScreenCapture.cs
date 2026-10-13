using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Lighting.Ambient;
using Deckle.Shell;
using Deckle.Vision;

namespace Deckle.Playground;

// ─── AmbientPage — screen capture ───────────────────────────────────────────
//
// A local capture, distinct from the AmbientEngine's own ScreenCaptureService:
// it samples frames and counts FPS without engaging the Hue pipeline. The
// FPS counter reads FrameCount once a second on the UI thread, so no frame
// is marshalled through the DispatcherQueue.

public sealed partial class AmbientPage
{
    private void OnScreenCaptureToggleClick(object sender, RoutedEventArgs e)
    {
        DecklePlaygroundSource.Log.ScreenCaptureToggled(_screenCapture is { IsRunning: true });

        if (_screenCapture is { IsRunning: true })
        {
            StopScreenCaptureIfRunning();
            return;
        }

        StartScreenCaptureService();
    }

    private bool StartScreenCaptureService()
    {
        if (_screenCapture is { IsRunning: true }) return true;

        try
        {
            _screenCapture ??= new ScreenCaptureService();
            _screenCapture.Stopped += OnScreenCaptureStopped;
            var targetMonitor = AmbientSettingsService.Instance.Current.SelectedMonitorDeviceName;
            _screenCapture.Start(targetMonitor);

            _screenCaptureLastSampledFrames = 0;
            _screenCaptureLastSampleTimestamp = Stopwatch.GetTimestamp();
            ScreenCaptureToggleIcon.Glyph = Glyphs.Transport.Stop;
            ScreenCaptureToggleLabel.Text = "Stop";
            ScreenCaptureStatusText.Text = "Running";
            ScreenCaptureStatusDot.Fill = GetThemeBrush("SystemFillColorSuccessBrush");
            ScreenCaptureFramesText.Text = "0";
            ScreenCaptureFpsText.Text = "—";
            _screenCaptureFpsTimer.Start();
            EvaluatePreviewTimerState();
            return true;
        }
        catch (Exception ex)
        {
            DecklePlaygroundSource.Log.ScreenCaptureStartFailed();
            DecklePlaygroundSource.Log.ScreenCaptureStartFailedDetail($"{ex.GetType().Name}: {ex.Message}");
            ScreenCaptureStatusText.Text = $"Failed: {ex.Message}";
            ScreenCaptureStatusDot.Fill = GetThemeBrush("SystemFillColorCriticalBrush");
            StopScreenCaptureIfRunning();
            return false;
        }
    }

    private void StopScreenCaptureIfRunning()
    {
        _screenCaptureFpsTimer.Stop();

        if (_screenCapture is null) return;

        _screenCapture.Stopped -= OnScreenCaptureStopped;
        _screenCapture.Dispose();
        _screenCapture = null;

        if (ScreenCaptureToggleButton is not null)
        {
            ScreenCaptureToggleIcon.Glyph = Glyphs.Transport.Play;
            ScreenCaptureToggleLabel.Text = "Start";
            ScreenCaptureStatusText.Text = "Stopped";
            ScreenCaptureStatusDot.Fill = GetThemeBrush("SystemFillColorNeutralBrush");
        }
        EvaluatePreviewTimerState();
    }

    private void OnScreenCaptureStopped()
    {
        // Fires on the capture worker thread after a sustained failure to
        // recreate the duplication.
        DispatcherQueue.TryEnqueueObserved(
            operation: "engine-state-sync", caller: "playground-ambient-page-capture",
            callback: () => StopScreenCaptureIfRunning(),
            rejectSource: "PLAYGROUND", rejectWhat: "screen capture stop");
    }

    private void OnScreenCaptureFpsTick(object? sender, object e)
    {
        if (_screenCapture is not { IsRunning: true }) return;

        long currentFrames = _screenCapture.FrameCount;
        long currentTimestamp = Stopwatch.GetTimestamp();
        long deltaFrames = currentFrames - _screenCaptureLastSampledFrames;
        long deltaMs = (currentTimestamp - _screenCaptureLastSampleTimestamp) * 1000 / Stopwatch.Frequency;

        double fps = deltaMs > 0 ? deltaFrames * 1000.0 / deltaMs : 0.0;

        ScreenCaptureFramesText.Text = currentFrames.ToString();
        ScreenCaptureFpsText.Text = $"{fps:F1}";

        _screenCaptureLastSampledFrames = currentFrames;
        _screenCaptureLastSampleTimestamp = currentTimestamp;
    }
}
