using System.Diagnostics;
using Windows.Graphics.DirectX;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    // Map a negotiated DXGI surface format to the WinRT pixel format the
    // FrameSampler keys its tone-map path on. Only the two formats the
    // duplication priority lists request are distinguished — FP16 scRGB
    // (HDR) and BGRA8 (SDR / everything else). Shared by Start and the
    // format-aware recreate so the derivation lives in one place.
    private static DirectXPixelFormat MapDxgiFormat(uint dxgiFormat)
        => dxgiFormat == ScreenCaptureInterop.DXGI_FORMAT_R16G16B16A16_FLOAT
            ? DirectXPixelFormat.R16G16B16A16Float
            : DirectXPixelFormat.B8G8R8A8UIntNormalized;

    // Reopen the duplication after it was invalidated (ACCESS_LOST,
    // ACCESS_DENIED, SESSION_DISCONNECTED). Retry forever with a 2 s
    // backoff until either DuplicateOutput1 succeeds — meaning the
    // user has returned from the secure desktop / unplugged the
    // headset / cleared the UAC prompt / etc. — or the engine is
    // cancelled. Never returns false ; the only exits are "succeeded"
    // (with _duplicationPtr set) and "cancelled" (with _duplicationPtr
    // still 0 and the caller seeing ct.IsCancellationRequested).
    private void TryRecreateDuplication(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested && _duplicationPtr == 0)
        {
            attempt++;
            try
            {
                // Re-detect the display's HDR state fresh on every attempt.
                // The interruption that invalidated the duplication is often
                // an HDR desktop toggle, which flips BOTH the negotiable
                // pixel format (FP16 scRGB ↔ BGRA8) and the peak luminance.
                // Reusing the Start-time snapshot would request a format
                // priority list that no longer matches the desktop — the
                // root of the silent HDR→SDR freeze : the duplication then
                // delivers BGRA8 while the sampler, never told, keeps tone-
                // mapping it as FP16. DetectHdrState builds a throwaway DXGI
                // factory each call, which is required — a factory predating
                // the mode change reports stale colour-space state.
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

                // Read back the negotiated format and compare against the
                // surface the consumer's sampler was built against. Either a
                // format flip (HDR↔SDR) or a resize invalidates the sampler's
                // GPU textures, so we surface a single FormatChanged signal
                // covering both — without it, TryRecreateDuplication used to
                // recover the duplication but leave the pipeline assuming the
                // stale format/size for the rest of the session.
                uint oldDxgiFormat = _activeDxgiFormat;
                uint newDxgiFormat = desc.ModeDesc.Format;
                bool formatChanged = newDxgiFormat != oldDxgiFormat;
                bool sizeChanged = newSize.Width != _lastSize.Width
                                || newSize.Height != _lastSize.Height;

                _activeDxgiFormat = newDxgiFormat;
                _activeFormat = MapDxgiFormat(newDxgiFormat);

                if (sizeChanged)
                {
                    DeckleVisionSource.Log.DuplicationResizeDetected(
                        _lastSize.Width, _lastSize.Height, newSize.Width, newSize.Height);
                    _lastSize = newSize;
                }

                // Cross-cutting Resource sub-provider: re-acquire a new
                // duplication after invalidation. The handle differs from the
                // previous one (Marshal.Release was already called upstream on
                // the old value; the matching ResourceReleased event was
                // emitted in CaptureLoop's ACCESS_LOST / SECURE_DESKTOP branch
                // or by the failed previous attempt finalizer).
                _duplicationAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "duplication-output", (long)_duplicationPtr, 0, "capture-loop");

                DeckleVisionSource.Log.DuplicationRecreated(
                    attempt, _lastSize.Width, _lastSize.Height);

                if (formatChanged)
                {
                    DeckleVisionSource.Log.CaptureFormatRenegotiated();
                    DeckleVisionSource.Log.CaptureFormatRenegotiatedDetail(
                        MapDxgiFormat(oldDxgiFormat).ToString(),
                        _activeFormat.ToString(),
                        _isHdrSession ? "on" : "off",
                        _peakLuminance,
                        _lastSize.Width, _lastSize.Height, attempt);
                }

                // Raise after the duplication is fully live and the active
                // format/size fields are committed, so the consumer's rebuild
                // reads consistent ActiveFormat / ContentSize / PeakLuminance.
                if (formatChanged || sizeChanged)
                {
                    FormatChanged?.Invoke();
                }
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                DeckleVisionSource.Log.DuplicationRecreateAttemptFailed();
                DeckleVisionSource.Log.DuplicationRecreateAttemptFailedDetail(
                    attempt, ex.GetType().Name, ex.Message);
                try { Task.Delay(RecreateBackoffMs, ct).Wait(ct); }
                catch (OperationCanceledException)
                {
                    // Stop() cancelled while waiting for the next recreate
                    // attempt. age_ms is relative to the session; there is no
                    // dedicated anchor for TryRecreateDuplication.
                    long ageMs = _startTimestamp != 0
                        ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                        : -1;
                    DeckleCancellationSource.Log.OperationCancelled(
                        "vision-capture", "upstream", (int)ageMs);
                    return;
                }
            }
        }
    }

}
