using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    // Every release is best effort so one failure does not leak the rest;
    // a failure stays a Verbose detail because the session is ending
    // anyway and the user gets nothing different.
    private void DisposeInternals()
    {
        if (_duplicationPtr != 0)
        {
            // Emitted before Release so the event is not lost if Release
            // throws.
            bool resourceDetailOpen = _duplicationAcquiredTicks != 0
                && OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleResourceSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Resource);
            if (resourceDetailOpen)
            {
                long releasedHandle = (long)_duplicationPtr;
                int ageMs = (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                    * 1000L / Stopwatch.Frequency);
                DeckleResourceSource.Log.ResourceReleased(
                    "duplication-output", releasedHandle, ageMs, "capture-loop");
            }
            try { Marshal.Release(_duplicationPtr); }
            catch (Exception ex) { ReportCleanupFailure("release_duplication", ex); }
            _duplicationPtr = 0;
            _duplicationAcquiredTicks = 0;
        }
        if (_output5Ptr != 0)
        {
            try { Marshal.Release(_output5Ptr); }
            catch (Exception ex) { ReportCleanupFailure("release_output", ex); }
            _output5Ptr = 0;
        }
        if (_adapterPtr != 0)
        {
            try { Marshal.Release(_adapterPtr); }
            catch (Exception ex) { ReportCleanupFailure("release_adapter", ex); }
            _adapterPtr = 0;
        }
        if (_d3dDevicePtr != 0)
        {
            try { Marshal.Release(_d3dDevicePtr); }
            catch (Exception ex) { ReportCleanupFailure("release_device", ex); }
            _d3dDevicePtr = 0;
        }
        if (_device is not null)
        {
            // The CsWinRT projection maps IClosable to IDisposable; disposing
            // frees the D3D11 device now instead of at finalization.
            try { (_device as IDisposable)?.Dispose(); }
            catch (Exception ex) { ReportCleanupFailure("dispose_device", ex); }
            _device = null;
        }
        _hmon = 0;
    }

    private static void ReportCleanupFailure(string step, Exception ex)
        => DeckleVisionSource.Log.CaptureCleanupFailed(step, ex.GetType().Name, ex.Message);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
