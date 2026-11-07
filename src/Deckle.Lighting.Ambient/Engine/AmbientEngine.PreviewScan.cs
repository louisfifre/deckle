// AmbientEngine — the preview scan: the screen capture and the sampler with
// no output, so the Settings preview shows what Ambient Light reads while it
// is off and no light receives anything.
using System.Diagnostics;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    // Why in the engine and not in the preview: the scan and a run capture
    // the same display, and the run must find it released. The engine owns
    // both, so it can order them. The scan fills the run's own slots
    // (_capture, _sampler), which keeps the preview accessors (LatestSample,
    // SourceSize, CapturedFrames, IsHdr) and the frame handlers the same in
    // both; it never builds an output, so nothing reaches a light.
    //
    // Ordering. Every scan transition holds _scanGate. A scan starts only
    // while the engine is Off. StartAsync sets Starting first, then ends the
    // scan under the gate (EndPreviewScanAsync) before it builds its capture:
    // a scan already inside the gate finishes starting and is ended right
    // after; one that enters later sees Starting and declines. The engine
    // returns to Off only once its run has released the display, and the
    // preview asks for a scan again on that state.
    //
    // One owner at a time, the Settings preview: a second StartPreviewScan
    // finds the scan running and returns true without starting another.
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private bool _scanning;
    private string _scanId = "";
    private long _scanStartTimestamp;

    /// <summary>True while the preview scan captures the display with no
    /// output. Safe to read from any thread.</summary>
    public bool IsPreviewScanning => Volatile.Read(ref _scanning);

    /// <summary>Starts capturing the display for the preview, with no
    /// output, while the engine is off. Returns true when the scan runs
    /// (already, or from this call), false when the engine is not off or the
    /// capture could not start; the failure is logged. The capture's own
    /// start (device, duplication) runs off the caller's thread.</summary>
    public async Task<bool> StartPreviewScanAsync()
    {
        if (_disposed) return false;

        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_scanning) return true;

            // Off with nothing owned: a run in any other state holds or is
            // about to hold the display.
            if (_disposed || IsRunning || _state != AmbientEngineState.Off || _capture is not null)
            {
                DeckleAmbientSource.Log.PreviewScanSkipped(_state.ToString());
                return false;
            }

            var ambient = _host.Ambient;
            string scanId = WorkId.Next();
            int rateHz = AmbientCaptureRate.Snap(ambient.CaptureFrameRateHz);
            DeckleAmbientSource.Log.PreviewScanStarting(scanId, rateHz);
            long startedAt = Stopwatch.GetTimestamp();

            var capture = new ScreenCaptureService(rateHz);
            AttachScanHandlers(capture);
            _capture = capture;
            StartFrameClock(startedAt);

            try
            {
                string? monitor = ambient.SelectedMonitorDeviceName;
                await Task.Run(() =>
                {
                    capture.Start(monitor);
                    EnsureSampler(capture);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The preview keeps its drawn frame; nothing else depends on
                // the scan, so this is a degradation, not a stop.
                DeckleAmbientSource.Log.PreviewScanStartFailed(scanId, ex.GetType().Name, ex.Message);
                await ReleaseScanDepsAsync().ConfigureAwait(false);
                return false;
            }

            _scanId = scanId;
            _scanStartTimestamp = startedAt;
            Volatile.Write(ref _scanning, true);
            return true;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <summary>Stops the preview scan and releases the display. A no-op
    /// when no scan runs. reason = an <see cref="AmbientPreviewScanEndReasons"/>
    /// value.</summary>
    public Task StopPreviewScanAsync(string reason = AmbientPreviewScanEndReasons.Closed)
        => EndPreviewScanAsync(reason);

    private async Task EndPreviewScanAsync(string reason)
    {
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_scanning) return;
            Volatile.Write(ref _scanning, false);

            long frames = _capture?.FrameCount ?? 0;
            long firstFrameMs = FirstFrameMs();
            Exception? failure = await ReleaseScanDepsAsync().ConfigureAwait(false);

            double durationS = (Stopwatch.GetTimestamp() - _scanStartTimestamp) / (double)Stopwatch.Frequency;
            DeckleAmbientSource.Log.PreviewScanStopped(
                _scanId, reason, failure is null ? "completed" : "failed",
                durationS, frames, firstFrameMs,
                failure?.GetType().Name ?? "none", failure?.Message ?? "none");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    // The run's release order: the capture first, which waits for its loop
    // to exit, so no frame handler runs any more; then the sampler, read
    // again after that wait in case the last frame built one
    // (EnsureSampler). Returns the first release failure, each one also
    // reported on its own.
    private async Task<Exception?> ReleaseScanDepsAsync()
    {
        Exception? firstFailure = null;

        var capture = _capture;
        if (capture is not null)
        {
            DetachScanHandlers(capture);
            // Dispose waits up to 2 s for the loop: off the caller's thread.
            try { await Task.Run(capture.Dispose).ConfigureAwait(false); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("scan_capture", ex); }
            _capture = null;
        }

        var sampler = Interlocked.Exchange(ref _sampler, null);
        if (sampler is not null)
        {
            try { await sampler.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { firstFailure ??= ReportReleaseFailure("scan_sampler", ex); }
        }

        return firstFailure;
    }

    private void AttachScanHandlers(ScreenCaptureService capture)
    {
        capture.FrameArrived += OnScanFrameArrived;
        capture.FrameProcessingFailed += OnFrameProcessingFailed;
        capture.Stopped += OnScanCaptureStopped;
        capture.FormatChanged += OnCaptureFormatChanged;
    }

    private void DetachScanHandlers(ScreenCaptureService capture)
    {
        capture.FrameArrived -= OnScanFrameArrived;
        capture.FrameProcessingFailed -= OnFrameProcessingFailed;
        capture.Stopped -= OnScanCaptureStopped;
        capture.FormatChanged -= OnCaptureFormatChanged;
    }

    // Capture thread. No push loop runs during a scan, so the exposure the
    // loop hands the sampler on each tick is handed here, per frame: the
    // preview's cells then react to the Exposure setting as a run's do.
    private void OnScanFrameArrived(CapturedFrame frame)
    {
        _sampler?.SetExposureEv(_host.Ambient.ExposureEv);
        OnFrameArrived(frame);
    }

    // Capture thread, raised from the loop's own exit: the release waits for
    // that loop, so it runs elsewhere. Vision has logged the cause.
    private void OnScanCaptureStopped(string reason)
        => _ = Task.Run(() => EndPreviewScanAsync(AmbientPreviewScanEndReasons.CaptureLost));
}
