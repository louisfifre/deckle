using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            bool vertical = message == LowLevelMouseHookInterop.WM_MOUSEWHEEL;
            bool horizontal = message == LowLevelMouseHookInterop.WM_MOUSEHWHEEL;

            uint mouseData = vertical || horizontal
                ? Marshal.PtrToStructure<LowLevelMouseHookInterop.MSLLHOOKSTRUCT>(lParam).mouseData
                : 0;

            // A low-level hook must return promptly or Windows can silently remove
            // it. Button consumers may persist a typing span, so their signal is
            // queued by the router; wheel publication stays the existing bounded path.
            _mouseInteractions.ObserveHookMessage(message, mouseData);
        }

        return LowLevelMouseHookInterop.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void QueuePointerInteraction() => RawInputInterop.PostThreadMessage(
        _threadId, WM_APP_POINTER_DOWN, IntPtr.Zero, IntPtr.Zero);

    private void PublishPointerInteraction()
    {
        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled) _rollupPointerDowns++;
        PointerInteraction?.Invoke();
        if (rollupEnabled) TrackRollup(RawInputHost.NowMs);
    }

    private void PublishWheelInteraction(WheelAxis axis, short delta)
    {
        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled) _rollupWheel++;
        WheelObserved?.Invoke(new MouseWheelEvent(
            Axis: axis,
            Delta: delta,
            TimestampMs: RawInputHost.NowMs,
            Device: IntPtr.Zero,
            Source: WheelEventSource.MessageHook));
        if (rollupEnabled) TrackRollup(RawInputHost.NowMs);
    }
}
