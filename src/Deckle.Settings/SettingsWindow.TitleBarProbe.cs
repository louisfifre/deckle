using System;
using Microsoft.UI.Dispatching;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ─── TitleBar layout probe ────────────────────────────────────────────────────
//
// Geometry snapshot of the title bar, emitted (debounced) after every zone resize
// and search-presentation swap. The bar renders at window sizes and DPIs no one
// is watching; the probe turns what it actually did — how wide the search zone
// and box were, where the Logs command landed, what the caption inset reserved —
// into a trace that can be read back after a manual resize pass. Pure observation:
// nothing here feeds back into layout.

public sealed partial class SettingsWindow
{
    private DispatcherQueueTimer? _titleBarProbe;

    // One quiet layout beat: resizes come in bursts, only the settled geometry
    // is worth a trace.
    private const int TitleBarProbeDebounceMs = 300;

    private void InitializeTitleBarProbe()
    {
        _titleBarProbe = DispatcherQueue.CreateTimer();
        _titleBarProbe.Interval = TimeSpan.FromMilliseconds(TitleBarProbeDebounceMs);
        _titleBarProbe.IsRepeating = false;
        _titleBarProbe.Tick += (_, _) => EmitTitleBarLayout();
    }

    private void ScheduleTitleBarProbe()
    {
        if (_titleBarProbe is null) return;
        _titleBarProbe.Stop();
        _titleBarProbe.Start();
    }

    private void EmitTitleBarLayout()
    {
        // Logs command position relative to the bar's left edge, in DIPs — read
        // together with bar width and the caption inset, it exposes the dead band
        // (min drag region + inset) to the right of the button.
        double logsX = LogsButton.TransformToVisual(AppTitleBar)
            .TransformPoint(default).X;
        double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        double insetRight = AppWindow.TitleBar.RightInset / scale;

        DeckleSettingsSource.Log.TitleBarLayout(
            (int)AppTitleBar.ActualWidth,
            (int)SearchZone.ActualWidth,
            (int)SearchBox.ActualWidth,
            (int)logsX,
            (int)LogsButton.ActualWidth,
            (int)insetRight,
            _searchPresentation.ToString());
    }
}
