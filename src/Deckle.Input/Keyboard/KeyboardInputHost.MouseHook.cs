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

            var hook = vertical || horizontal
                ? Marshal.PtrToStructure<LowLevelMouseHookInterop.MSLLHOOKSTRUCT>(lParam)
                : default;

            // A low-level hook must return promptly or Windows can silently remove
            // it. Button consumers may persist a typing span, so their signal is
            // queued by the router. The wheel path only classifies and offers the
            // value to its bounded interceptor; observations are handed to the
            // pump for correlation with Raw Input after the callback returns.
            bool intercepted = _mouseInteractions.ObserveHookMessage(
                message, hook);
            if (intercepted) return new IntPtr(1);
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

    private bool PublishWheelInteraction(
        WheelAxis axis,
        short delta,
        LowLevelMouseHookInterop.MSLLHOOKSTRUCT hook)
    {
        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled) _rollupWheel++;
        var wheelEvent = new MouseWheelEvent(
            Axis: axis,
            Delta: delta,
            TimestampMs: MouseWheelTimestamp.ToSharedClock(
                hook.time,
                unchecked((uint)Environment.TickCount),
                RawInputHost.NowMs),
            Device: IntPtr.Zero,
            Source: WheelEventSource.MessageHook,
            IsInjected: (hook.flags & (LowLevelMouseHookInterop.LLMHF_INJECTED
                | LowLevelMouseHookInterop.LLMHF_LOWER_IL_INJECTED)) != 0,
            InputState: LowLevelMouseHookInterop.GetWheelInputState());
        bool observationQueued = QueueHookWheelObservation(in wheelEvent);
        bool intercepted = observationQueued
            && (Volatile.Read(ref _wheelInterceptor)?.Intercept(in wheelEvent) ?? false);
        if (rollupEnabled) TrackRollup(RawInputHost.NowMs);
        return intercepted;
    }
}
