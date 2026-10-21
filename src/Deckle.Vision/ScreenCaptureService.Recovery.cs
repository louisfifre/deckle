using System.Diagnostics;
using System.Diagnostics.Tracing;
using Windows.Graphics.DirectX;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    // DXGI only ever returns one of the two requested formats, so anything
    // that is not FP16 is BGRA8.
    private static DirectXPixelFormat MapDxgiFormat(uint dxgiFormat)
        => dxgiFormat == ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT
            ? DirectXPixelFormat.R16G16B16A16Float
            : DirectXPixelFormat.B8G8R8A8UIntNormalized;

    // Expected Windows unavailability retries without limit; unexpected
    // failures stop after MaxUnexpectedRecreateAttempts and a dead device
    // stops at once. A false return without cancellation has already
    // reported its terminal milestone; abandonReason names it for the
    // loop's exit line and the stopped milestone.
    private bool TryRecreateDuplication(CancellationToken ct, out string abandonReason)
    {
        abandonReason = CaptureStopReasons.Requested;
        int attempt = 0;
        long incidentStartedTicks = 0;
        bool incidentOpen = false;
        while (!ct.IsCancellationRequested && _duplicationPtr == 0)
        {
            attempt++;
            bool formatChanged;
            bool sizeChanged;
            uint oldDxgiFormat;
            try
            {
                // Re-detect HDR on every attempt: the interruption is often an
                // HDR desktop toggle, which flips both the pixel format and
                // the peak luminance. DetectHdrState needs its fresh DXGI
                // factory: one created before the mode change reports a stale
                // colour space.
                var hdr = ScreenCaptureInterop.DetectHdrState(_hmon);
                _isHdrSession  = hdr.IsHdr;
                _peakLuminance = hdr.PeakLuminance;

                uint[] formatList = _isHdrSession ? HdrFormats : SdrFormats;
                _duplicationPtr = ScreenCaptureInterop.DuplicateOutput1(
                    _output5Ptr, _d3dDevicePtr, formatList);

                var desc = ScreenCaptureInterop.GetDuplicationDesc(_duplicationPtr);
                var newSize = new Windows.Graphics.SizeInt32
                {
                    Width  = (int)desc.ModeDesc.Width,
                    Height = (int)desc.ModeDesc.Height,
                };

                // A format flip or a resize invalidates the sampler's GPU
                // textures. Without FormatChanged the sampler keeps reading
                // the new surface with the old layout and an HDR↔SDR toggle
                // freezes the output silently.
                oldDxgiFormat = _activeDxgiFormat;
                uint newDxgiFormat = desc.ModeDesc.Format;
                formatChanged = newDxgiFormat != oldDxgiFormat;
                sizeChanged = newSize.Width != _lastSize.Width
                           || newSize.Height != _lastSize.Height;

                _activeDxgiFormat = newDxgiFormat;
                _activeFormat = MapDxgiFormat(newDxgiFormat);

                if (sizeChanged)
                {
                    DeckleVisionSource.Log.DuplicationResizeDetected(
                        _lastSize.Width, _lastSize.Height, newSize.Width, newSize.Height);
                    _lastSize = newSize;
                }

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

                DeckleVisionSource.Log.DuplicationRecreated(
                    attempt, _lastSize.Width, _lastSize.Height);

                if (incidentOpen)
                {
                    DeckleVisionSource.Log.DuplicationRecreateRecovered();
                    if (incidentStartedTicks != 0)
                    {
                        long durationMs = (Stopwatch.GetTimestamp() - incidentStartedTicks)
                            * 1000L / Stopwatch.Frequency;
                        DeckleVisionSource.Log.DuplicationRecreateEpisodeDetail(
                            "recovered", attempt - 1, durationMs);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_duplicationPtr != 0)
                {
                    ReleaseDuplicationForRecreate();
                }

                bool detailOpen = OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleVisionSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Capture);
                if (detailOpen && incidentStartedTicks == 0)
                {
                    incidentStartedTicks = Stopwatch.GetTimestamp();
                }

                bool fatalDevice = IsFatalDeviceFailure(ex.HResult);
                bool expected = IsExpectedRecreateFailure(ex.HResult);

                if (!fatalDevice && attempt == 2)
                {
                    DeckleVisionSource.Log.DuplicationRecreateAttemptFailed(
                        attempt, ex.HResult, ex.GetType().Name, ex.Message);
                    incidentOpen = true;
                }

                if (detailOpen)
                {
                    DeckleVisionSource.Log.DuplicationRecreateAttemptFailedDetail(
                        attempt, ex.GetType().Name, ex.Message);
                }

                if (fatalDevice || (!expected && attempt >= MaxUnexpectedRecreateAttempts))
                {
                    if (fatalDevice)
                    {
                        DeckleVisionSource.Log.DeviceLost(ex.HResult);
                        abandonReason = CaptureStopReasons.DeviceLost;
                    }
                    else
                    {
                        DeckleVisionSource.Log.DuplicationRecreateAbandoned(
                            attempt, ex.HResult, ex.GetType().Name, ex.Message);
                        abandonReason = CaptureStopReasons.RecreateAbandoned;
                    }

                    if (incidentOpen && incidentStartedTicks != 0)
                    {
                        long durationMs = (Stopwatch.GetTimestamp() - incidentStartedTicks)
                            * 1000L / Stopwatch.Frequency;
                        DeckleVisionSource.Log.DuplicationRecreateEpisodeDetail(
                            "abandoned", attempt, durationMs);
                    }
                    return false;
                }

                int backoffMs = attempt < MaxUnexpectedRecreateAttempts
                    ? RecreateInitialBackoffMs
                    : RecreateExtendedBackoffMs;
                try { Task.Delay(backoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    // age_ms is relative to the session: the recreate has no
                    // anchor of its own.
                    long ageMs = _startTimestamp != 0
                        ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                        : -1;
                    DeckleCancellationSource.Log.OperationCancelled(
                        CancelledOperations.VisionCapture, CancellationReasons.Upstream, (int)ageMs);
                    return false;
                }
                continue;
            }

            if (formatChanged)
            {
                // A display mode change the user caused (HDR toggle): one
                // milestone, its before/after behind the detail gate.
                DeckleVisionSource.Log.CaptureFormatRenegotiated();
                DeckleVisionSource.Log.CaptureFormatRenegotiatedDetail(
                    MapDxgiFormat(oldDxgiFormat).ToString(),
                    _activeFormat.ToString(),
                    _isHdrSession ? "on" : "off",
                    _peakLuminance,
                    _lastSize.Width, _lastSize.Height, attempt);
            }

            // Raised outside the recreate try: a throwing handler used to
            // land in the failure branch, release a healthy duplication and
            // count as a failed attempt. Raised after the duplication is
            // live and the format/size fields are committed, so the
            // consumer's rebuild reads consistent ActiveFormat / ContentSize
            // / PeakLuminance.
            if (formatChanged || sizeChanged)
            {
                try
                {
                    FormatChanged?.Invoke();
                }
                catch (Exception ex)
                {
                    DeckleVisionSource.Log.SubscriberThrew(
                        "format_changed", ex.GetType().Name, ex.Message);
                }
            }
            return true;
        }

        return _duplicationPtr != 0;
    }

    private static bool IsFatalDeviceFailure(int hr)
        => hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_REMOVED
        || hr == ScreenCaptureInterop.DXGI_ERROR_DEVICE_HUNG;

    private static bool IsExpectedRecreateFailure(int hr)
        => hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_LOST
        || hr == ScreenCaptureInterop.DXGI_ERROR_ACCESS_DENIED
        || hr == ScreenCaptureInterop.DXGI_ERROR_SESSION_DISCONNECTED
        || hr == ScreenCaptureInterop.DXGI_ERROR_NOT_CURRENTLY_AVAILABLE
        || hr == ScreenCaptureInterop.DXGI_ERROR_INVALID_CALL
        || hr == ScreenCaptureInterop.DXGI_ERROR_UNSUPPORTED
        || hr == ScreenCaptureInterop.E_ACCESSDENIED;

}
