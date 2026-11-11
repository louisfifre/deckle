using System.Globalization;
using Deckle.Catalog;
using Deckle.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Deckle.Localization;

namespace Deckle.Shell.TaskbarCover;

// Draws the primary taskbar's display, the taskbar on it and the approach
// zone the setting reaches past it, to scale. The zone comes from the same
// CoverGeometry.ApproachZone the cover host builds its hit test with, at the
// display's own scale, so the drawing is the zone the cursor will meet, not
// an illustration of it. Shows and never writes: the page hands it the
// distance, the saved one and each one the slider shows during a drag.
public sealed partial class ApproachPreview : UserControl
{
    private const string Library = "Deckle.Shell.TaskbarCover";

    // The outer limit of the zone, in the page's own units: thin enough to
    // read as a line, thick enough to stay visible at the smallest distance.
    private const double LimitThickness = 2;

    private TaskbarPlacement? _placement;
    private int _approachDip = ApproachDistance.DefaultDip;

    public ApproachPreview() => InitializeComponent();

    /// <summary>
    /// The distance drawn, in device-independent pixels. Brought into
    /// ApproachDistance's range, as the setting is.
    /// </summary>
    public int ApproachDistanceDip
    {
        get => _approachDip;
        set
        {
            value = ApproachDistance.Clamp(value);
            if (value == _approachDip) return;
            _approachDip = value;
            ShowReach();
            Layout();
        }
    }

    // Reads where the primary taskbar sits now: its rect, its display and
    // that display's scale, through the cover host's own locator. The page
    // calls it on each visit and when the cover turns on, the moments the
    // preview comes into view; a display change while the page stays open is
    // picked up at the next visit. No primary taskbar (Explorer restarting)
    // leaves the block's empty text in the stage's place.
    public void Refresh()
    {
        List<TaskbarPlacement> placements = TaskbarLocator.Locate();
        _placement = placements.Count > 0 ? placements[0] : null;
        Block.IsLive = _placement is not null;
        ShowDisplay();
        ShowReach();
        Layout();
    }

    private void StageRoot_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void ShowDisplay()
    {
        if (_placement is not { } p) { DisplayReadout.Value = null; return; }
        NativeMethods.RECT monitor = p.MonitorRect;
        DisplayReadout.Value = Format("TaskbarCoverApproachPreview_Display_Format",
            monitor.right - monitor.left,
            monitor.bottom - monitor.top,
            (int)Math.Round(p.DpiScale * 100));
    }

    // The distance in this display's physical pixels: the figure the cover
    // host measures the cursor against, so the readout says what the DIP
    // field means on this screen.
    private void ShowReach()
    {
        if (_placement is not { } p) { ReachReadout.Value = null; return; }
        ReachReadout.Value = Format("TaskbarCoverApproachPreview_Reach_Format",
            CoverGeometry.ApproachSpan(_approachDip, p.DpiScale, p.MonitorRect, p.Edge));
    }

    // One factor maps the display's physical pixels onto the stage, the
    // largest that fits the room both ways, so the frame keeps the screen's
    // ratio and the taskbar and the zone keep their true share of it.
    private void Layout()
    {
        if (_placement is not { } p) return;

        NativeMethods.RECT monitor = p.MonitorRect;
        double monitorWidth  = monitor.right - monitor.left;
        double monitorHeight = monitor.bottom - monitor.top;
        double roomWidth  = StageRoot.ActualWidth;
        double roomHeight = StageRoot.ActualHeight;
        if (monitorWidth <= 0 || monitorHeight <= 0 || roomWidth <= 0 || roomHeight <= 0) return;

        double scale = Math.Min(roomWidth / monitorWidth, roomHeight / monitorHeight);
        double stageWidth  = Math.Round(monitorWidth * scale);
        double stageHeight = Math.Round(monitorHeight * scale);
        Stage.Width  = stageWidth;
        Stage.Height = stageHeight;
        Place(ScreenShape, 0, 0, stageWidth, stageHeight);

        // An auto-hidden taskbar may sit partly off its display, and a long
        // distance on a narrow display may run past it: both are drawn
        // within the frame.
        var zone    = OnStage(ClipTo(CoverGeometry.ApproachZone(p.Rect, p.Edge, _approachDip, p.DpiScale, monitor), monitor), monitor, scale);
        var taskbar = OnStage(ClipTo(p.Rect, monitor), monitor, scale);

        Place(ZoneShape, zone.Left, zone.Top, zone.Right - zone.Left, zone.Bottom - zone.Top);
        Place(TaskbarShape, taskbar.Left, taskbar.Top, taskbar.Right - taskbar.Left, taskbar.Bottom - taskbar.Top);

        // The limit sits on the zone's side away from the screen edge, where
        // the band is opaque again.
        double width  = zone.Right - zone.Left;
        double height = zone.Bottom - zone.Top;
        switch (p.Edge)
        {
            case TaskbarEdge.Left:
                Place(ZoneLimitShape, zone.Right - LimitThickness, zone.Top, LimitThickness, height);
                break;
            case TaskbarEdge.Right:
                Place(ZoneLimitShape, zone.Left, zone.Top, LimitThickness, height);
                break;
            case TaskbarEdge.Top:
                Place(ZoneLimitShape, zone.Left, zone.Bottom - LimitThickness, width, LimitThickness);
                break;
            default:
                Place(ZoneLimitShape, zone.Left, zone.Top, width, LimitThickness);
                break;
        }
    }

    private static NativeMethods.RECT ClipTo(NativeMethods.RECT r, NativeMethods.RECT bounds) => new()
    {
        left   = Math.Clamp(r.left,   bounds.left, bounds.right),
        top    = Math.Clamp(r.top,    bounds.top,  bounds.bottom),
        right  = Math.Clamp(r.right,  bounds.left, bounds.right),
        bottom = Math.Clamp(r.bottom, bounds.top,  bounds.bottom),
    };

    // Rounded to whole page units, so the edges land on the page's pixel
    // grid instead of blurring across two.
    private static (double Left, double Top, double Right, double Bottom) OnStage(
        NativeMethods.RECT r, NativeMethods.RECT monitor, double scale) =>
        (Math.Round((r.left   - monitor.left) * scale),
         Math.Round((r.top    - monitor.top)  * scale),
         Math.Round((r.right  - monitor.left) * scale),
         Math.Round((r.bottom - monitor.top)  * scale));

    private static void Place(Rectangle shape, double left, double top, double width, double height)
    {
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        shape.Width  = Math.Max(0, width);
        shape.Height = Math.Max(0, height);
    }

    private static string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Loc.GetFrom(Library, key), args);
}
