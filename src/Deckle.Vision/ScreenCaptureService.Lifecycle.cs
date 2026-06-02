using System.Diagnostics;
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

            try
            {
                _hmon = ResolveTargetMonitor(targetMonitorDeviceName);
                if (_hmon == 0)
                {
                    throw new InvalidOperationException(
                        "Primary monitor not found — MonitorFromPoint returned NULL.");
                }

                // Find the DXGI adapter + output5 driving this monitor,
                // capture the HDR state in passing. Throws if no match
                // (display disconnected mid-startup).
                var match = ScreenCaptureInterop.FindDxgiOutputForMonitor(_hmon);
                _adapterPtr    = match.AdapterPtr;
                _output5Ptr    = match.Output5Ptr;
                _isHdrSession  = match.Hdr.IsHdr;
                _peakLuminance = match.Hdr.PeakLuminance;

                // Create the D3D11 device on that specific adapter —
                // mandatory for DuplicateOutput1 (E_INVALIDARG otherwise
                // on multi-GPU laptops where the default adapter
                // doesn't drive the target monitor).
                _device = ScreenCaptureInterop.CreateDirect3DDevice(_adapterPtr);
                _d3dDevicePtr = ScreenCaptureInterop.GetD3D11Device(_device);

                // DuplicateOutput1 with the format priority list that
                // matches the OS's current display mode. The negotiated
                // format is read back via GetDuplicationDesc.
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

                // Sub-provider transverse Resource — acquire de la duplication
                // output. size_bytes=0 parce que c'est un handle de
                // synchronisation, pas une allocation mémoire mesurable.
                _duplicationAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "duplication-output", (long)_duplicationPtr, 0, "capture-loop");

                _frameCount = 0;
                _startTimestamp = Stopwatch.GetTimestamp();

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _captureLoopTask = Task.Run(() => CaptureLoop(token), token);

                IsRunning = true;

                DeckleVisionSource.Log.CaptureSessionConfigured(
                    (long)_hmon, _lastSize.Width, _lastSize.Height, _activeFormat.ToString(),
                    _isHdrSession ? "on" : "off", _peakLuminance, (int)AcquireTimeoutMs, ThrottleIntervalMs);
                DeckleVisionSource.Log.ScreenCaptureStarted();
            }
            catch (Exception ex)
            {
                DeckleVisionSource.Log.CaptureStartFailed(ex.GetType().Name, ex.Message);
                DeckleVisionSource.Log.CaptureStartFailedDetail(ex.HResult, ex.GetType().Name, ex.Message);

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
        bool wasRunning;
        lock (_lock)
        {
            if (!IsRunning && _duplicationPtr == 0) return;

            wasRunning = IsRunning;
            loopTask = _captureLoopTask;
            cts = _cts;

            // Flip the state first so a concurrent FrameArrived
            // consumer sees IsRunning=false even before the loop
            // actually wraps up.
            IsRunning = false;
            _captureLoopTask = null;
            _cts = null;
        }

        // Cancel the loop outside the lock — Wait might re-enter
        // via Stopped event subscribers.
        try { cts?.Cancel(); } catch { /* best effort */ }
        try { loopTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            // Expected — cooperative cancellation. Trace l'OCE attendu sur le
            // sub-provider transverse Cancellation : Stop() a explicitement
            // demandé l'arrêt, la boucle a propagé. `age_ms` reflète la durée
            // entière de la session de capture car le worker a tourné de Start
            // jusqu'au moment du cancel.
            long ageMs = _startTimestamp != 0
                ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                : -1;
            DeckleCancellationSource.Log.OperationCancelled(
                "vision-capture", "upstream", (int)ageMs);
        }
        catch (Exception ex)
        {
            DeckleVisionSource.Log.CaptureLoopWaitFailed(ex.GetType().Name, ex.Message);
        }
        try { cts?.Dispose(); } catch { /* best effort */ }

        lock (_lock)
        {
            long endTimestamp = Stopwatch.GetTimestamp();
            long durationMs = (endTimestamp - _startTimestamp) * 1000 / Stopwatch.Frequency;
            long frames = Interlocked.Read(ref _frameCount);
            double fpsAvg = durationMs > 0 ? frames * 1000.0 / durationMs : 0.0;
            double durationSec = durationMs / 1000.0;

            DisposeInternals();

            if (wasRunning)
            {
                DeckleVisionSource.Log.ScreenCaptureStopped(frames, durationSec);
                DeckleVisionSource.Log.ScreenCaptureStoppedDetail(frames, durationMs, fpsAvg);
            }
        }
    }

}
