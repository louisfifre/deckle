using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    public void Start(string? targetMonitorDeviceName = null)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning) return;

            DeckleVisionSource.Log.ScreenCaptureStarting();

            // Names the step a start failure stopped at, so the failure line
            // tells a missing monitor from a driver refusing duplication.
            string stage = "resolve_monitor";
            try
            {
                _hmon = ResolveTargetMonitor(targetMonitorDeviceName);
                if (_hmon == 0)
                {
                    throw new InvalidOperationException(
                        "Primary monitor not found — MonitorFromPoint returned NULL.");
                }

                stage = "find_output";
                var match = ScreenCaptureInterop.FindDxgiOutputForMonitor(_hmon);
                _adapterPtr    = match.AdapterPtr;
                _output5Ptr    = match.Output5Ptr;
                _isHdrSession  = match.Hdr.IsHdr;
                _peakLuminance = match.Hdr.PeakLuminance;

                // The device must live on the adapter that drives this
                // monitor, or DuplicateOutput1 fails with E_INVALIDARG.
                stage = "create_device";
                _device = ScreenCaptureInterop.CreateDirect3DDevice(_adapterPtr, out int featureLevel);
                _d3dDevicePtr = ScreenCaptureInterop.GetD3D11Device(_device);

                stage = "duplicate_output";
                uint[] formatList = _isHdrSession ? HdrFormats : SdrFormats;
                _duplicationPtr = ScreenCaptureInterop.DuplicateOutput1(
                    _output5Ptr, _d3dDevicePtr, formatList);

                var desc = ScreenCaptureInterop.GetDuplicationDesc(_duplicationPtr);
                _lastSize = new Windows.Graphics.SizeInt32
                {
                    Width  = (int)desc.ModeDesc.Width,
                    Height = (int)desc.ModeDesc.Height,
                };
                _activeDxgiFormat = desc.ModeDesc.Format;
                _activeFormat = MapDxgiFormat(_activeDxgiFormat);

                // size_bytes 0: the duplication is a handle, not a
                // measurable allocation.
                bool resourceDetailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleResourceSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Resource);
                _duplicationAcquiredTicks = resourceDetailOpen ? Stopwatch.GetTimestamp() : 0;
                if (resourceDetailOpen)
                {
                    DeckleResourceSource.Log.ResourceAcquired(
                        "duplication-output", (long)_duplicationPtr, 0, "capture-loop");
                }

                stage = "start_loop";
                _frameCount = 0;
                _startTimestamp = Stopwatch.GetTimestamp();
                _loopExitReason = null;
                Volatile.Write(ref _lastFrameFailure, null);

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureLoopTask = Task.Run(() => CaptureLoop(token), token);

                IsRunning = true;
                _sessionStarted = true;

                if (DeckleVisionSource.Log.IsEnabled())
                {
                    DeckleVisionSource.Log.ScreenCaptureStarted(
                        ScreenCaptureInterop.GetAdapterDescription(_adapterPtr),
                        ScreenCaptureInterop.FormatFeatureLevel(featureLevel),
                        ScreenCaptureInterop.GetMonitorDeviceName(_hmon),
                        _lastSize.Width, _lastSize.Height, _activeFormat.ToString(),
                        _isHdrSession ? "on" : "off", _targetFrameRateHz);
                }

                if (OperationalLogAdmission.IsScopedDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleVisionSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
                {
                    DeckleVisionSource.Log.CaptureSessionConfigured(
                        (long)_hmon, _lastSize.Width, _lastSize.Height, _activeFormat.ToString(),
                        _isHdrSession ? "on" : "off", _peakLuminance, (int)AcquireTimeoutMs,
                        (int)Math.Round(1000.0 / _targetFrameRateHz));
                }
            }
            catch (Exception ex)
            {
                DeckleVisionSource.Log.CaptureStartFailed(
                    stage, ex.HResult, ex.GetType().Name, ex.Message);

                DisposeInternals();
                throw;
            }
        }
    }

    private nint ResolveTargetMonitor(string? targetMonitorDeviceName)
    {
        if (string.IsNullOrEmpty(targetMonitorDeviceName))
        {
            return ScreenCaptureInterop.GetPrimaryMonitor();
        }

        var resolved = ScreenCaptureInterop.FindMonitorByDeviceName(targetMonitorDeviceName);
        if (resolved != 0)
        {
            DeckleVisionSource.Log.TargetMonitorResolved(targetMonitorDeviceName, (long)resolved);
            return resolved;
        }

        DeckleVisionSource.Log.MonitorNotFound(targetMonitorDeviceName);
        return ScreenCaptureInterop.GetPrimaryMonitor();
    }

    public void Stop()
    {
        Task? loopTask;
        CancellationTokenSource? cts;
        bool sessionStarted;
        lock (_lock)
        {
            if (!IsRunning && !_sessionStarted && _duplicationPtr == 0) return;

            sessionStarted = _sessionStarted;
            loopTask = _captureLoopTask;
            cts = _cts;

            // Flip the state first so a concurrent FrameArrived
            // consumer sees IsRunning=false even before the loop
            // actually wraps up.
            IsRunning = false;
            _sessionStarted = false;
            _captureLoopTask = null;
            _cts = null;
        }

        // Cancel the loop outside the lock — Wait might re-enter
        // via Stopped event subscribers.
        try { cts?.Cancel(); }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.CaptureCleanupFailed("cancel_loop", ex.GetType().Name, ex.Message);
        }

        long waitStartTicks = Stopwatch.GetTimestamp();
        try
        {
            if (loopTask is not null && !loopTask.Wait(TimeSpan.FromSeconds(2)))
            {
                DeckleVisionSource.Log.CaptureLoopStopTimedOut(
                    (Stopwatch.GetTimestamp() - waitStartTicks) * 1000 / Stopwatch.Frequency);
            }
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Expected cooperative cancellation; age_ms spans the whole
            // capture session.
            if (OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleCancellationSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Lifecycle))
            {
                long ageMs = _startTimestamp != 0
                    ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                    : -1;
                DeckleCancellationSource.Log.OperationCancelled(
                    "vision-capture", "upstream", (int)ageMs);
            }
        }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.CaptureLoopWaitFailed(ex.GetType().Name, ex.Message);
        }

        try { cts?.Dispose(); }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.CaptureCleanupFailed("dispose_cts", ex.GetType().Name, ex.Message);
        }

        lock (_lock)
        {
            long durationMs = 0;
            long frames = 0;
            if (sessionStarted)
            {
                durationMs = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency;
                frames = Interlocked.Read(ref _frameCount);
            }

            DisposeInternals();

            if (sessionStarted)
            {
                // A loop that never ran (cancelled before its first
                // iteration) wrote no reason: the stop was requested.
                DeckleVisionSource.Log.ScreenCaptureStopped(
                    _loopExitReason ?? "requested", durationMs);

                if (OperationalLogAdmission.IsScopedDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleVisionSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)(Keywords.Capture | Keywords.Lifecycle)))
                {
                    double fpsAvg = durationMs > 0 ? frames * 1000.0 / durationMs : 0.0;
                    DeckleVisionSource.Log.ScreenCaptureStoppedDetail(frames, durationMs, fpsAvg);
                }
            }
        }
    }

}
