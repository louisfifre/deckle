using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Globalization.NumberFormatting;
using Windows.System;

namespace Deckle.Catalog;

// ── CurveEditor ───────────────────────────────────────────────────────────────
//
// The one curve component of the settings design system (decision 21, the
// Playground Segmentation form): a plot of a two-point cubic Bézier with its two
// draggable handles, taking the width it is given, and beside it the four values
// as fields in a two-by-two grid with the `cubic-bezier(…)` readout under them.
// Below a comfortable width the fields move under the plot.
//
// Two layouts from one frame (CurveFrame): with axes, the plot spans the data
// space (rulers on "nice" steps, unit labels, dashed guides at the box's edges)
// and the unit square sits where the frame puts it — the dictation's decay from
// the ramp start to the ramp end; without axes, the plot is the unit square
// alone, square, the Ambient brightness response. A dashed reference shows the
// straight line between the fixed endpoints, two faint stems tie each endpoint
// to its handle, and an optional floor guide draws a level the engine holds.
//
// Writes follow the gesture rule: a handle moves the drawing while it is dragged
// and writes the points at its release; a field writes at its commit; an arrow
// key on a focused handle nudges by a hundredth (a tenth with Shift) and writes
// at once. X stays in [0,1] so the curve remains a function; Y may overshoot by a
// quarter when AllowOutside is set. The drawing is a tree of XAML shapes updated
// in place on every change, with no Win2D (decision 3).
public sealed partial class CurveEditor : UserControl
{
    private const int SampleCount = 96;
    private const double HandleSize = 24.0;
    private const double HandleRadius = HandleSize / 2.0;
    private const double GutterLeft = 46.0;
    private const double GutterBottom = 24.0;
    private const double PadTop = 12.0;
    private const double PadRight = 14.0;
    private const double Pad = 6.0;
    private const double WideHeight = 280.0;
    private const double SquareSide = 220.0;
    private const double StackBelowWidth = 560.0;
    private const double Outside = 0.25;

    private readonly Grid _root = new();
    private readonly Border _plotFrame;
    private readonly Canvas _plot = new();
    private readonly Polyline _curve;
    private readonly Line _reference;
    private readonly Line _floor;
    private readonly Line _stem1;
    private readonly Line _stem2;
    private readonly Line _guideStart;
    private readonly Line _guideEnd;
    private readonly List<FrameworkElement> _rulers = new();
    private readonly Thumb _handle1;
    private readonly Thumb _handle2;
    private readonly Grid _fields = new();
    private readonly NumberBox _x1Box, _y1Box, _x2Box, _y2Box;
    private readonly TextBlock _readout;

    // The handle positions while a drag is in flight, in unit coordinates; the
    // dependency properties are written at the release.
    private double _dragX1, _dragY1, _dragX2, _dragY2;
    private bool _dragging;
    private bool _syncing;

    public event EventHandler? PointsCommitted;

    public CurveEditor()
    {
        _curve = new Polyline
        {
            StrokeThickness = 1.75,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _reference = new Line { StrokeThickness = 1.0, StrokeDashArray = new DoubleCollection { 3.0, 3.0 } };
        _floor = new Line { StrokeThickness = 1.0, StrokeDashArray = new DoubleCollection { 2.0, 3.0 }, Opacity = 0.85 };
        _stem1 = new Line { StrokeThickness = 1.0, Opacity = 0.4 };
        _stem2 = new Line { StrokeThickness = 1.0, Opacity = 0.4 };
        _guideStart = new Line { StrokeThickness = 1.0, StrokeDashArray = new DoubleCollection { 2.0, 2.0 } };
        _guideEnd = new Line { StrokeThickness = 1.0, StrokeDashArray = new DoubleCollection { 2.0, 2.0 } };

        _handle1 = BuildHandle(first: true);
        _handle2 = BuildHandle(first: false);

        _plot.Children.Add(_reference);
        _plot.Children.Add(_guideStart);
        _plot.Children.Add(_guideEnd);
        _plot.Children.Add(_floor);
        _plot.Children.Add(_curve);
        _plot.Children.Add(_stem1);
        _plot.Children.Add(_stem2);
        _plot.Children.Add(_handle1);
        _plot.Children.Add(_handle2);
        _plot.SizeChanged += (_, _) => Redraw();

        // The plot's frame: the layer fill with a card stroke, the same box the
        // Playground gives its curve.
        _plotFrame = new Border
        {
            Padding = new Thickness(Pad),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Height = WideHeight,
            Child = _plot,
        };

        // The four fields: x1 y1 over x2 y2, each with a caption, the readout
        // under them in a monospace caption as the Playground prints it.
        _x1Box = BuildField();
        _y1Box = BuildField();
        _x2Box = BuildField();
        _y2Box = BuildField();
        _readout = new TextBlock
        {
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 8, 0, 0),
        };
        BuildFieldGrid();

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.ColumnSpacing = 16;
        _root.RowSpacing = 12;
        _root.Children.Add(_plotFrame);
        _root.Children.Add(_fields);
        Content = _root;

        SizeChanged += (_, e) => Reflow(e.NewSize.Width);
        ActualThemeChanged += (_, _) => ApplyBrushes();
        Loaded += (_, _) => { ApplyBrushes(); Reflow(ActualWidth); Redraw(); SyncFields(); };
    }

    // ── Properties ───────────────────────────────────────────────────────────

    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(double), typeof(CurveEditor), new PropertyMetadata(0.42, OnPointChanged));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(double), typeof(CurveEditor), new PropertyMetadata(0.0, OnPointChanged));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(double), typeof(CurveEditor), new PropertyMetadata(0.58, OnPointChanged));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(double), typeof(CurveEditor), new PropertyMetadata(1.0, OnPointChanged));

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    // The four values as one record, the currency of the Curve setting.
    public CurvePoints Points
    {
        get => new(X1, Y1, X2, Y2);
        set
        {
            _syncing = true;
            try
            {
                X1 = value.X1; Y1 = value.Y1; X2 = value.X2; Y2 = value.Y2;
            }
            finally { _syncing = false; }
            Redraw();
            SyncFields();
        }
    }

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(CurveFrame), typeof(CurveEditor),
        new PropertyMetadata(CurveFrame.Unit, (d, _) => ((CurveEditor)d).OnFrameChanged()));

    // Where the unit square sits and whether the plot carries axes.
    public CurveFrame Frame
    {
        get => (CurveFrame?)GetValue(FrameProperty) ?? CurveFrame.Unit;
        set => SetValue(FrameProperty, value);
    }

    public static readonly DependencyProperty FloorProperty = DependencyProperty.Register(
        nameof(Floor), typeof(double?), typeof(CurveEditor),
        new PropertyMetadata(null, (d, _) => ((CurveEditor)d).Redraw()));

    // An optional level in data y the engine holds, drawn as a dashed guide.
    public double? Floor
    {
        get => (double?)GetValue(FloorProperty);
        set => SetValue(FloorProperty, value);
    }

    public static readonly DependencyProperty AllowOutsideProperty = DependencyProperty.Register(
        nameof(AllowOutside), typeof(bool), typeof(CurveEditor),
        new PropertyMetadata(false, (d, _) => ((CurveEditor)d).ApplyFieldBounds()));

    // Lets a handle's Y leave [0,1] by a quarter.
    public bool AllowOutside
    {
        get => (bool)GetValue(AllowOutsideProperty);
        set => SetValue(AllowOutsideProperty, value);
    }

    private static void OnPointChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (CurveEditor)d;
        if (self._syncing) return;
        self.Redraw();
        self.SyncFields();
    }

    private void OnFrameChanged()
    {
        bool axes = Frame.Axes;
        _plotFrame.Height = axes ? WideHeight : SquareSide;
        _plotFrame.Width = axes ? double.NaN : SquareSide;
        _plotFrame.HorizontalAlignment = axes ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        _guideStart.Visibility = axes ? Visibility.Visible : Visibility.Collapsed;
        _guideEnd.Visibility = axes ? Visibility.Visible : Visibility.Collapsed;
        Redraw();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private void Reflow(double width)
    {
        bool stack = width > 0 && width < StackBelowWidth;
        Grid.SetRow(_plotFrame, 0);
        Grid.SetColumn(_plotFrame, 0);
        Grid.SetColumnSpan(_plotFrame, stack ? 2 : 1);
        Grid.SetRow(_fields, stack ? 1 : 0);
        Grid.SetColumn(_fields, stack ? 0 : 1);
        _fields.VerticalAlignment = stack ? VerticalAlignment.Top : VerticalAlignment.Center;
    }

    private void BuildFieldGrid()
    {
        _fields.ColumnSpacing = 12;
        _fields.RowSpacing = 8;
        _fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddField("x1", _x1Box, 0, 0);
        AddField("y1", _y1Box, 0, 1);
        AddField("x2", _x2Box, 1, 0);
        AddField("y2", _y2Box, 1, 1);
        Grid.SetRow(_readout, 2);
        Grid.SetColumnSpan(_readout, 2);
        _fields.Children.Add(_readout);
    }

    private void AddField(string label, NumberBox box, int row, int column)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });
        stack.Children.Add(box);
        Grid.SetRow(stack, row);
        Grid.SetColumn(stack, column);
        _fields.Children.Add(stack);
    }

    private NumberBox BuildField()
    {
        var box = new NumberBox
        {
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            NumberFormatter = new DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = 2,
                IsGrouped = false,
                NumberRounder = new IncrementNumberRounder
                {
                    Increment = 0.01,
                    RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp,
                },
            },
        };
        // A field writes at its commit (Enter, focus loss, an arrow click), which
        // is what ValueChanged means on a NumberBox.
        box.ValueChanged += (_, _) => OnFieldCommitted();
        return box;
    }

    private void ApplyFieldBounds()
    {
        double min = AllowOutside ? -Outside : 0;
        double max = AllowOutside ? 1 + Outside : 1;
        _y1Box.Minimum = min; _y1Box.Maximum = max;
        _y2Box.Minimum = min; _y2Box.Maximum = max;
    }

    private void SyncFields()
    {
        if (_x1Box is null) return;
        _syncing = true;
        try
        {
            Set(_x1Box, X1); Set(_y1Box, Y1); Set(_x2Box, X2); Set(_y2Box, Y2);
            _readout.Text = $"cubic-bezier({X1:0.00}, {Y1:0.00}, {X2:0.00}, {Y2:0.00})";
        }
        finally { _syncing = false; }

        static void Set(NumberBox box, double value)
        {
            if (double.IsNaN(value)) return;
            if (box.Value != value) box.Value = value;
        }
    }

    private void OnFieldCommitted()
    {
        if (_syncing) return;
        if (double.IsNaN(_x1Box.Value) || double.IsNaN(_y1Box.Value) ||
            double.IsNaN(_x2Box.Value) || double.IsNaN(_y2Box.Value)) return;
        Commit(Snap(_x1Box.Value), Snap(_y1Box.Value), Snap(_x2Box.Value), Snap(_y2Box.Value));
    }

    // ── Handles ──────────────────────────────────────────────────────────────

    private Thumb BuildHandle(bool first)
    {
        var thumb = new Thumb
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thumb, first ? "Control point 1" : "Control point 2");

        // The handle's look: an accent dot of 14 px with a ring in the base
        // background colour so it reads as floating over the curve, in a 24 px
        // target. A template needs XAML, so this one is read from a string.
        const string template =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Thumb'>" +
            "<Grid Background='Transparent'><Ellipse Width='14' Height='14' HorizontalAlignment='Center' VerticalAlignment='Center' " +
            "Fill='{ThemeResource AccentFillColorDefaultBrush}' Stroke='{ThemeResource SolidBackgroundFillColorBaseBrush}' StrokeThickness='2' />" +
            "</Grid></ControlTemplate>";
        thumb.Template = (ControlTemplate)XamlReader.Load(template);

        thumb.DragStarted += (_, _) =>
        {
            _dragging = true;
            _dragX1 = X1; _dragY1 = Y1; _dragX2 = X2; _dragY2 = Y2;
        };
        thumb.DragDelta += (_, e) => OnHandleDragDelta(first, e);
        thumb.DragCompleted += (_, _) =>
        {
            _dragging = false;
            Commit(_dragX1, _dragY1, _dragX2, _dragY2);
        };
        thumb.KeyDown += (_, e) => OnHandleKeyDown(first, e);
        return thumb;
    }

    private void OnHandleDragDelta(bool first, DragDeltaEventArgs e)
    {
        PlotRect g = Geometry();
        if (!g.Valid) return;

        double u = first ? _dragX1 : _dragX2;
        double v = first ? _dragY1 : _dragY2;
        Point px = g.FromUnit(u, v);
        Point moved = new(px.X + e.HorizontalChange, px.Y + e.VerticalChange);
        (double nu, double nv) = g.ToUnit(moved);
        nu = Snap(Math.Clamp(nu, 0, 1));
        nv = Snap(ClampY(nv));
        if (first) { _dragX1 = nu; _dragY1 = nv; } else { _dragX2 = nu; _dragY2 = nv; }
        Redraw();
    }

    private void OnHandleKeyDown(bool first, KeyRoutedEventArgs e)
    {
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                      & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        double step = shift ? 0.1 : 0.01;
        double dx = 0, dy = 0;
        switch (e.Key)
        {
            case VirtualKey.Left: dx = -step; break;
            case VirtualKey.Right: dx = step; break;
            case VirtualKey.Up: dy = step; break;
            case VirtualKey.Down: dy = -step; break;
            default: return;
        }
        e.Handled = true;
        // Up moves the handle toward the end's level, whatever way the frame
        // points, so the key follows the drawing.
        double x1 = X1, y1 = Y1, x2 = X2, y2 = Y2;
        if (first) { x1 = Snap(Math.Clamp(x1 + dx, 0, 1)); y1 = Snap(ClampY(y1 + dy)); }
        else { x2 = Snap(Math.Clamp(x2 + dx, 0, 1)); y2 = Snap(ClampY(y2 + dy)); }
        Commit(x1, y1, x2, y2);
    }

    private void Commit(double x1, double y1, double x2, double y2)
    {
        bool changed = x1 != X1 || y1 != Y1 || x2 != X2 || y2 != Y2;
        _syncing = true;
        try { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
        finally { _syncing = false; }
        Redraw();
        SyncFields();
        if (changed) PointsCommitted?.Invoke(this, EventArgs.Empty);
    }

    private double ClampY(double v) => AllowOutside ? Math.Clamp(v, -Outside, 1 + Outside) : Math.Clamp(v, 0, 1);

    // The hundredth the fields step on, so a dragged value reads as 0.42, not
    // 0.4237.
    private static double Snap(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // ── Drawing ──────────────────────────────────────────────────────────────

    // The pixel frame of the plot, derived from the current size and the frame,
    // shared by the drawing, the handle placement and the drag so all agree.
    private readonly struct PlotRect
    {
        public readonly bool Valid;
        public readonly double Left, Right, Top, Bottom;
        public readonly double XMax, YMax;
        public readonly CurveFrame Frame;

        public PlotRect(double w, double h, CurveFrame frame)
        {
            Frame = frame;
            bool axes = frame.Axes;
            Left = axes ? GutterLeft : Pad;
            Right = w - (axes ? PadRight : Pad);
            Top = axes ? PadTop : Pad;
            Bottom = h - (axes ? GutterBottom : Pad);
            XMax = frame.XMax > 0 ? frame.XMax : 1;
            YMax = frame.YMax > 0 ? frame.YMax : 1;
            Valid = w > 0 && h > 0 && Right - Left >= 20 && Bottom - Top >= 20;
        }

        public double MapX(double x) => Left + (x / XMax) * (Right - Left);
        public double MapY(double y) => Bottom - (y / YMax) * (Bottom - Top);

        public Point FromUnit(double u, double v) => new(MapX(Frame.DataX(u)), MapY(Frame.DataY(v)));

        public (double U, double V) ToUnit(Point p)
        {
            double x = (p.X - Left) / (Right - Left) * XMax;
            double y = (Bottom - p.Y) / (Bottom - Top) * YMax;
            return (Frame.UnitU(x), Frame.UnitV(y));
        }
    }

    private PlotRect Geometry() => new(_plot.ActualWidth, _plot.ActualHeight, Frame);

    private void Redraw()
    {
        PlotRect g = Geometry();
        if (!g.Valid) return;

        double x1 = _dragging ? _dragX1 : X1, y1 = _dragging ? _dragY1 : Y1;
        double x2 = _dragging ? _dragX2 : X2, y2 = _dragging ? _dragY2 : Y2;
        var curve = new CubicBezier(x1, y1, x2, y2);

        // The reference: the straight line between the fixed endpoints.
        Point p0 = g.FromUnit(0, 0), p3 = g.FromUnit(1, 1);
        SetLine(_reference, p0, p3);

        // The curve across the whole data span: held before and after the box.
        _curve.Points.Clear();
        for (int i = 0; i <= SampleCount; i++)
        {
            double x = (double)i / SampleCount * g.XMax;
            double y = g.Frame.Sample(curve, x);
            _curve.Points.Add(new Point(g.MapX(x), g.MapY(y)));
        }

        // Stems and handles.
        Point h1 = g.FromUnit(x1, y1), h2 = g.FromUnit(x2, y2);
        SetLine(_stem1, p0, h1);
        SetLine(_stem2, p3, h2);
        Canvas.SetLeft(_handle1, h1.X - HandleRadius);
        Canvas.SetTop(_handle1, h1.Y - HandleRadius);
        Canvas.SetLeft(_handle2, h2.X - HandleRadius);
        Canvas.SetTop(_handle2, h2.Y - HandleRadius);

        // The floor guide.
        if (Floor is double floor)
        {
            double fy = g.MapY(floor);
            SetLine(_floor, new Point(g.Left, fy), new Point(g.Right, fy));
            _floor.Visibility = Visibility.Visible;
        }
        else
        {
            _floor.Visibility = Visibility.Collapsed;
        }

        // The box guides and the rulers, in the wide layout only.
        foreach (FrameworkElement ruler in _rulers) _plot.Children.Remove(ruler);
        _rulers.Clear();
        if (g.Frame.Axes)
        {
            double gx0 = g.MapX(g.Frame.OriginX), gx1 = g.MapX(g.Frame.EndX);
            SetLine(_guideStart, new Point(gx0, g.Top), new Point(gx0, g.Bottom));
            SetLine(_guideEnd, new Point(gx1, g.Top), new Point(gx1, g.Bottom));
            DrawRulers(g);
        }
    }

    private void DrawRulers(PlotRect g)
    {
        Brush grid = Brush("DividerStrokeColorDefaultBrush");
        Style? caption = Application.Current.Resources["CaptionTextBlockStyle"] as Style;
        Brush text = Brush("TextFillColorTertiaryBrush");

        double yStep = NiceStep(g.YMax, 4);
        for (double v = 0; yStep > 0 && v <= g.YMax + yStep * 1e-3; v += yStep)
        {
            double y = g.MapY(v);
            var line = new Line { X1 = g.Left, Y1 = y, X2 = g.Right, Y2 = y, Stroke = grid, StrokeThickness = 1, Opacity = 0.5 };
            AddRuler(line);
            var label = new TextBlock
            {
                Text = $"{v:0.#} {g.Frame.YUnit}".TrimEnd(),
                Style = caption,
                Foreground = text,
                FontSize = 11,
                TextAlignment = TextAlignment.Right,
                Width = GutterLeft - 8,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            AddRuler(label);
        }

        double xStep = NiceStep(g.XMax, 5);
        for (double v = 0; xStep > 0 && v <= g.XMax + xStep * 1e-3; v += xStep)
        {
            double x = g.MapX(v);
            var tick = new Line { X1 = x, Y1 = g.Bottom, X2 = x, Y2 = g.Bottom + 4, Stroke = grid, StrokeThickness = 1, Opacity = 0.5 };
            AddRuler(tick);
            var label = new TextBlock
            {
                Text = $"{v:0} {g.Frame.XUnit}".TrimEnd(),
                Style = caption,
                Foreground = text,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Width = 48,
            };
            Canvas.SetLeft(label, x - 24);
            Canvas.SetTop(label, g.Bottom + 6);
            AddRuler(label);
        }
    }

    private void AddRuler(FrameworkElement element)
    {
        // Rulers sit under everything else: inserted at the front of the canvas.
        _plot.Children.Insert(0, element);
        _rulers.Add(element);
    }

    // "Nice" tick step (1 / 2 / 5 × 10ⁿ) so a ruler lands on round values.
    private static double NiceStep(double range, int targetTicks)
    {
        if (range <= 0 || targetTicks <= 0) return 0;
        double raw = range / targetTicks;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double step = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
        return step * mag;
    }

    private static void SetLine(Line line, Point a, Point b)
    {
        line.X1 = a.X; line.Y1 = a.Y; line.X2 = b.X; line.Y2 = b.Y;
    }

    private void ApplyBrushes()
    {
        _plotFrame.Background = Brush("LayerFillColorDefaultBrush");
        _plotFrame.BorderBrush = Brush("CardStrokeColorDefaultBrush");
        _curve.Stroke = Brush("AccentFillColorDefaultBrush");
        _stem1.Stroke = Brush("AccentFillColorDefaultBrush");
        _stem2.Stroke = Brush("AccentFillColorDefaultBrush");
        _reference.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _guideStart.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _guideEnd.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _floor.Stroke = Brush("SystemFillColorCautionBrush");
        _readout.Foreground = Brush("TextFillColorSecondaryBrush");
        Redraw();
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
