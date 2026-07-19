using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Input;

namespace Deckle.Input;

public sealed partial class KeyboardInputHost
{
    private void WinEventProc(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (!_focusEvents.ShouldPublish(eventType, hwnd, idObject, idChild, dwmsEventTime))
            return;

        bool rollupEnabled = IsKeyboardRollupEnabled();
        if (rollupEnabled) _rollupFocusChanges++;
        FocusChanged?.Invoke();
        if (rollupEnabled) TrackRollup(RawInputHost.NowMs);
    }

}
