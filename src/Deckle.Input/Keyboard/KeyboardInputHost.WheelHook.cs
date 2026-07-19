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
            if (vertical || horizontal)
            {
                var hook = Marshal.PtrToStructure<LowLevelMouseHookInterop.MSLLHOOKSTRUCT>(lParam);
                bool wheelRollupEnabled = IsKeyboardRollupEnabled();
                if (wheelRollupEnabled) _rollupWheel++;
                WheelObserved?.Invoke(new MouseWheelEvent(
                    Axis:        vertical ? WheelAxis.Vertical : WheelAxis.Horizontal,
                    Delta:       LowLevelMouseHookInterop.GetWheelDelta(hook.mouseData),
                    TimestampMs: RawInputHost.NowMs,
                    Device:      IntPtr.Zero,
                    Source:      WheelEventSource.MessageHook));
                if (wheelRollupEnabled) TrackRollup(RawInputHost.NowMs);
            }
        }

        return LowLevelMouseHookInterop.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

}
