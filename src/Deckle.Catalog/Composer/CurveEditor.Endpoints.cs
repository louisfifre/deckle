using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Deckle.Catalog;

// ── CurveEditor: the endpoint handles ─────────────────────────────────────────
//
// The two fixed points of the Bézier, (0,0) and (1,1) of the unit square, are
// where the frame puts its origin and its end. With EndpointLimits set, each
// gets a handle that moves those frame coordinates: for the cut curve the
// origin is (ramp start, max wait) and the end (ramp end, min wait), so one
// handle carries two anchors and dragging the corner is dragging what the
// drawing shows. The handle is a square, the vector-drawing convention for a
// point on the curve; the control points stay round, the convention for a
// handle off it.
//
// The gesture rule holds: while a handle is dragged the frame is moved in a
// copy (_frameDrag) and the plot's data space stays the one the drag started
// in, so nothing rescales under the pointer; at the release the editor raises
// FrameCommitted with the moved frame and the owner writes its anchors, then
// hands back a frame recomputed from them. An arrow key moves one step of the
// anchor's grain (ten with Shift) and commits at once. CurveEndpointLimits
// keeps every move on the grain, in the bounds, and the frame drawable.
public sealed partial class CurveEditor
{
    private readonly Thumb _originHandle;
    private readonly Thumb _endHandle;

    // The frame an endpoint drag is moving, null when none is in flight.
    private CurveFrame? _frameDrag;
    private Point _endpointPixel;

    private CurveEndpointLimits? _endpointLimits;

    // Which frame coordinates the endpoint handles move, and within what; null
    // (the default) draws no endpoint handle. A plain property, set by the owner.
    public CurveEndpointLimits? EndpointLimits
    {
        get => _endpointLimits;
        set
        {
            _endpointLimits = value;
            Redraw();
        }
    }

    // The handles' accessible names ("Ramp start, Max wait"), read by a screen
    // reader with their values as the item status.
    public string? OriginHandleName
    {
        get => AutomationProperties.GetName(_originHandle);
        set => AutomationProperties.SetName(_originHandle, value ?? string.Empty);
    }

    public string? EndHandleName
    {
        get => AutomationProperties.GetName(_endHandle);
        set => AutomationProperties.SetName(_endHandle, value ?? string.Empty);
    }

    // Raised at the release of an endpoint drag, or at an arrow key on an
    // endpoint handle, with the frame the handle asks for. The owner writes the
    // values it binds to those coordinates and sets Frame back.
    public event EventHandler<CurveFrame>? FrameCommitted;

    private Thumb BuildEndpointHandle(bool origin)
    {
        var thumb = new Thumb
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
            Visibility = Visibility.Collapsed,
        };

        // A 12 px square with slightly rounded corners, the accent fill and the
        // ring in the base background colour the round handles wear, in the same
        // 24 px target.
        const string template =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Thumb'>" +
            "<Grid Background='Transparent'><Rectangle Width='13' Height='13' RadiusX='2' RadiusY='2' " +
            "HorizontalAlignment='Center' VerticalAlignment='Center' " +
            "Fill='{ThemeResource AccentFillColorDefaultBrush}' Stroke='{ThemeResource SolidBackgroundFillColorBaseBrush}' StrokeThickness='2' />" +
            "</Grid></ControlTemplate>";
        thumb.Template = (ControlTemplate)XamlReader.Load(template);

        thumb.DragStarted += (_, _) =>
        {
            _frameDrag = Frame;
            PlotRect g = Geometry();
            _endpointPixel = origin ? g.FromUnit(0, 0) : g.FromUnit(1, 1);
        };
        thumb.DragDelta += (_, e) => OnEndpointDragDelta(origin, e);
        thumb.DragCompleted += (_, _) =>
        {
            CurveFrame? moved = _frameDrag;
            _frameDrag = null;
            if (moved is not null && moved != Frame) FrameCommitted?.Invoke(this, moved);
            Redraw();
        };
        thumb.KeyDown += (_, e) => OnEndpointKeyDown(origin, e);
        return thumb;
    }

    private void OnEndpointDragDelta(bool origin, DragDeltaEventArgs e)
    {
        if (_frameDrag is null || _endpointLimits is null) return;
        PlotRect g = Geometry();
        if (!g.Valid) return;

        // The pointer, unsnapped, kept inside the plot's data space: a bound
        // beyond the visible axis (a ramp end of 230 s on a 140 s axis) is
        // reached by releasing, letting the axis grow, and dragging again.
        _endpointPixel = new Point(_endpointPixel.X + e.HorizontalChange, _endpointPixel.Y + e.VerticalChange);
        (double x, double y) = g.ToData(_endpointPixel);
        x = Math.Clamp(x, 0, g.XMax);
        y = Math.Clamp(y, 0, g.YMax);
        _frameDrag = origin
            ? _endpointLimits.MoveOrigin(_frameDrag, x, y)
            : _endpointLimits.MoveEnd(_frameDrag, x, y);
        Redraw();
    }

    private void OnEndpointKeyDown(bool origin, KeyRoutedEventArgs e)
    {
        if (_endpointLimits is null) return;
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                      & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        int step = shift ? 10 : 1;
        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case VirtualKey.Left: dx = -step; break;
            case VirtualKey.Right: dx = step; break;
            case VirtualKey.Up: dy = step; break;
            case VirtualKey.Down: dy = -step; break;
            default: return;
        }
        e.Handled = true;
        CurveFrame moved = origin
            ? _endpointLimits.NudgeOrigin(Frame, dx, dy)
            : _endpointLimits.NudgeEnd(Frame, dx, dy);
        if (moved != Frame) FrameCommitted?.Invoke(this, moved);
    }

    // Each endpoint handle on its point, shown only on an axes plot whose
    // owner lets that endpoint move; its values as the accessible item status.
    private void PlaceEndpoints(PlotRect g, Point p0, Point p3)
    {
        bool axes = g.Frame.Axes;
        bool origin = axes && _endpointLimits is { OriginMovable: true };
        bool end = axes && _endpointLimits is { EndMovable: true };
        _originHandle.Visibility = origin ? Visibility.Visible : Visibility.Collapsed;
        _endHandle.Visibility = end ? Visibility.Visible : Visibility.Collapsed;
        if (origin)
        {
            Canvas.SetLeft(_originHandle, p0.X - HandleRadius);
            Canvas.SetTop(_originHandle, p0.Y - HandleRadius);
            AutomationProperties.SetItemStatus(_originHandle, Status(g.Frame.OriginX, g.Frame.OriginY, g.Frame));
        }
        if (end)
        {
            Canvas.SetLeft(_endHandle, p3.X - HandleRadius);
            Canvas.SetTop(_endHandle, p3.Y - HandleRadius);
            AutomationProperties.SetItemStatus(_endHandle, Status(g.Frame.EndX, g.Frame.EndY, g.Frame));
        }

        static string Status(double x, double y, CurveFrame f)
            => $"{x:0.##} {f.XUnit}, {y:0.##} {f.YUnit}".Replace("  ", " ").Trim();
    }
}
