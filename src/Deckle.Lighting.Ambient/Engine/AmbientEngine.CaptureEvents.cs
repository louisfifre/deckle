// AmbientEngine — capture-pump event handlers (frame arrival, format
// renegotiation + sampler rebuild, fatal capture loss).
using System.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private const int FrameProcessingIncidentDelayMs = 1_000;
    private const int FrameProcessingFatalDelayMs = 5_000;
    private const int FrameProcessingGapCapMs = 250;

    private void OnFrameArrived(CapturedFrame frame)
    {
        EnsureFrameProcessingEpisodeSession();

        var sampler = _sampler;
        if (sampler is null) return;

        if (sampler.Process(frame))
        {
            CompleteFrameProcessingEpisode();
        }
        else
        {
            RecordFrameProcessingFailure();
        }
    }

    private void OnFrameProcessingFailed()
    {
        EnsureFrameProcessingEpisodeSession();
        RecordFrameProcessingFailure();
    }

    private void EnsureFrameProcessingEpisodeSession()
    {
        var capture = _capture;
        if (ReferenceEquals(_frameProcessingEpisodeCapture, capture)) return;

        _frameProcessingEpisodeCapture = capture;
        ResetFrameProcessingEpisode();
        Interlocked.Exchange(ref _frameProcessingStopRequested, 0);
    }

    private void RecordFrameProcessingFailure()
    {
        long now = Stopwatch.GetTimestamp();
        if (_frameProcessingLastFailureTicks != 0)
        {
            long gapTicks = now - _frameProcessingLastFailureTicks;
            long gapCapTicks = Stopwatch.Frequency * FrameProcessingGapCapMs / 1000L;
            _frameProcessingActiveFailureTicks += Math.Min(gapTicks, gapCapTicks);
        }

        _frameProcessingLastFailureTicks = now;
        _frameProcessingFailures++;

        long activeFailureMs = _frameProcessingActiveFailureTicks
            * 1000L / Stopwatch.Frequency;
        if (!_frameProcessingIncidentOpen
            && activeFailureMs >= FrameProcessingIncidentDelayMs)
        {
            _frameProcessingIncidentOpen = true;
            DeckleAmbientSource.Log.FrameProcessingIncidentOpened();
            DeckleAmbientSource.Log.FrameProcessingEpisodeDetail(
                "opened", _frameProcessingFailures, activeFailureMs);
        }

        if (_frameProcessingIncidentOpen
            && activeFailureMs >= FrameProcessingIncidentDelayMs + FrameProcessingFatalDelayMs
            && Interlocked.Exchange(ref _frameProcessingStopRequested, 1) == 0)
        {
            DeckleAmbientSource.Log.FrameProcessingEpisodeDetail(
                "abandoned", _frameProcessingFailures, activeFailureMs);
            AbortStartOrStop(
                "frame_processing_failed",
                DeckleAmbientSource.Log.FrameProcessingFailed);
        }
    }

    private void CompleteFrameProcessingEpisode()
    {
        if (Volatile.Read(ref _frameProcessingStopRequested) != 0) return;

        if (_frameProcessingIncidentOpen)
        {
            long activeFailureMs = _frameProcessingActiveFailureTicks
                * 1000L / Stopwatch.Frequency;
            DeckleAmbientSource.Log.FrameProcessingRecovered();
            DeckleAmbientSource.Log.FrameProcessingEpisodeDetail(
                "recovered", _frameProcessingFailures, activeFailureMs);
        }

        ResetFrameProcessingEpisode();
    }

    private void ResetFrameProcessingEpisode()
    {
        _frameProcessingLastFailureTicks = 0;
        _frameProcessingActiveFailureTicks = 0;
        _frameProcessingFailures = 0;
        _frameProcessingIncidentOpen = false;
    }

    // Capture worker thread — raised from inside TryRecreateDuplication
    // after the duplication renegotiated a surface (HDR↔SDR toggle or a
    // resolution change) the live FrameSampler was not built for. Rebuild
    // the sampler against the fresh format / size / peak luminance so the
    // pipeline recovers instead of freezing on dead output. Runs on the
    // same worker thread that raises FrameArrived, so the swap can't race a
    // Process() call — no frame is mid-flight through the old sampler when
    // we replace it. The next push-loop tick re-applies the live exposure
    // to the new sampler (PushLoopAsync top), so we don't forward it here.
    private void OnCaptureFormatChanged()
    {
        var capture = _capture;
        var device = capture?.Device;
        if (capture is null || device is null) return;

        FrameSampler fresh;
        try
        {
            fresh = new FrameSampler(
                device,
                capture.ContentSize,
                capture.ActiveFormat,
                capture.PeakLuminance);
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.SamplerRebuildFailed();
            DeckleAmbientSource.Log.SamplerRebuildFailedDetail(ex.GetType().Name, ex.Message);
            return;
        }

        // Publish the new sampler atomically. UI-thread readers (preview
        // grid + tuning panel via LatestSample / GridCols / ContentPeak)
        // see either the old or the new instance — both valid — and the
        // superseded one's volatile snapshots stay readable even after
        // dispose, so the swap never throws on the read side.
        var superseded = Interlocked.Exchange(ref _sampler, fresh);

        DeckleAmbientSource.Log.SamplerRebuilt();
        DeckleAmbientSource.Log.SamplerRebuiltDetail(capture.IsHdrSession ? "HDR" : "SDR");

        if (superseded is not null)
        {
            // Fire-and-forget : the superseded sampler is no longer reachable
            // from Process (we swapped before the next FrameArrived) so
            // releasing its GPU textures is safe. DisposeAsync completes
            // synchronously (COM release only) ; the continuation is guarded
            // so a release fault surfaces as a Warning rather than an
            // unobserved task exception.
            _ = DisposeSupersededSamplerAsync(superseded);
        }
    }

    private static async Task DisposeSupersededSamplerAsync(FrameSampler sampler)
    {
        try { await sampler.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.SamplerRebuildFailed();
            DeckleAmbientSource.Log.SamplerRebuildFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // Capture worker thread → marshal off it via Task.Run because Stop()
    // raises StateChanged whose subscribers (AmbientPage, tray) expect
    // to live on their own dispatchers, and Stop() is not designed to
    // run synchronously on the capture's loop thread.
    private void OnCaptureStopped()
    {
        AbortStartOrStop("capture_lost", DeckleAmbientSource.Log.CaptureLost);
    }
}
