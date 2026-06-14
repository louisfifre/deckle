using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;

namespace Deckle.Hud;

// HudWindow — positioning, no-activate show, top-most z-order, and the
// delayed z-order diagnostic emit.
public sealed partial class HudWindow : Window
{
    // Pixel rect the HUD would occupy at the current DPI + work area +
    // Overlay.Position setting, regardless of visibility. HudOverlayManager
    // reads this to lay out the stack even when the HUD itself is hidden.
    public Windows.Graphics.RectInt32 GetRectPx()
    {
        var wa = DisplayArea.Primary.WorkArea;

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        double scale = dpi / 96.0;

        int w = (int)Math.Round(HUD_WIDTH  * scale);
        int h = (int)Math.Round(HUD_HEIGHT * scale);
        int margin = (int)Math.Round(HUD_BOTTOM_MARGIN * scale);

        // HUD centered horizontally by design (mirrors native Win11 HUDs —
        // volume, brightness, screen capture). Only vertical anchor is user-
        // configurable. StartsWith covers legacy corner values from older
        // settings.json files.
        string position = Settings.SettingsService.Instance.Current.Overlay.Position ?? "";
        int x = wa.X + (wa.Width - w) / 2;
        int y = position.StartsWith("Top")
            ? wa.Y + margin
            : wa.Y + wa.Height - h - margin;

        return new Windows.Graphics.RectInt32(x, y, w, h);
    }

    private void ShowNoActivate()
    {
        // Recomputed on every show: a Windows DPI scale change between two
        // dictations (125% → 150%) is reflected immediately.
        var rect = GetRectPx();
        AppWindow.MoveAndResize(rect);

        WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", "before_show_noactivate");
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", "after_show_noactivate");
        bool setposOk = NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW);
        int setposError = setposOk ? 0 : Marshal.GetLastWin32Error();
        WindowingProbe.EmitWindowZOrderState(
            _hwnd, "hud", "after_setwindowpos_topmost",
            setposOk, setposError);
        int zOrderProbeGeneration = ++_zOrderProbeGeneration;
        EmitDelayedZOrderState("after_setwindowpos_topmost_50ms", 50, zOrderProbeGeneration, setposOk, setposError);
        EmitDelayedZOrderState("after_setwindowpos_topmost_250ms", 250, zOrderProbeGeneration, setposOk, setposError);

        // Windowing: emitted after MoveAndResize + ShowWindow to capture the
        // effective post-DWM rect. `anchor` reflects the
        // Settings.Overlay.Position setting (BottomCenter default, TopCenter
        // alternative); DPI/work area/horizontal centering wrapping lives in
        // GetRectPx, but we capture the result rather than the intent to allow
        // reversal through dpi.
        string position = Settings.SettingsService.Instance.Current.Overlay.Position ?? "";
        string anchor = position.StartsWith("Top") ? "TopCenter" : "BottomCenter";
        WindowingProbe.EmitWindowPositioned(_hwnd, "hud", anchor);
    }

    private void EmitDelayedZOrderState(string stage, int delayMs, int generation, bool setposOk, int setposError)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        timer.IsRepeating = false;
        timer.Tick += (sender, _) =>
        {
            sender.Stop();
            if (generation != _zOrderProbeGeneration) return;
            WindowingProbe.EmitWindowZOrderState(_hwnd, "hud", stage, setposOk, setposError);
        };
        timer.Start();
    }
}
