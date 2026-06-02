using System.Diagnostics;
using System.Runtime.InteropServices;
using Deckle.Diagnostics;

namespace Deckle.Vision;

public sealed partial class ScreenCaptureService
{
    private void DisposeInternals()
    {
        if (_duplicationPtr != 0)
        {
            // Sub-provider transverse Resource — release de la duplication
            // sur Stop / Dispose. age calculé depuis le dernier acquire
            // (Start ou TryRecreateDuplication). Émis avant le Release
            // pour ne pas perdre l'event si le Release lève.
            long releasedHandle = (long)_duplicationPtr;
            int ageMs = (int)((Stopwatch.GetTimestamp() - _duplicationAcquiredTicks)
                               * 1000L / Stopwatch.Frequency);
            DeckleResourceSource.Log.ResourceReleased(
                "duplication-output", releasedHandle, ageMs, "capture-loop");
            try { Marshal.Release(_duplicationPtr); } catch { /* best effort */ }
            _duplicationPtr = 0;
        }
        if (_output5Ptr != 0)
        {
            try { Marshal.Release(_output5Ptr); } catch { /* best effort */ }
            _output5Ptr = 0;
        }
        if (_adapterPtr != 0)
        {
            try { Marshal.Release(_adapterPtr); } catch { /* best effort */ }
            _adapterPtr = 0;
        }
        if (_d3dDevicePtr != 0)
        {
            try { Marshal.Release(_d3dDevicePtr); } catch { /* best effort */ }
            _d3dDevicePtr = 0;
        }
        if (_device is not null)
        {
            // IDirect3DDevice implements IDisposable through IClosable in
            // CsWinRT projection. Release here so the underlying D3D11
            // device is freed promptly.
            try { (_device as IDisposable)?.Dispose(); } catch { /* best effort */ }
            _device = null;
        }
        _hmon = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }}
