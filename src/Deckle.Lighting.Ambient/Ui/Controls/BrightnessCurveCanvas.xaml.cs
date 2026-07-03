using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Deckle.Lighting.Ambient;

// Compact editor for the Ambient brightness-response curve. X is the
// sampled max-channel input, Y is the pushed Hue bri response, both in
// normalised [0,1] space. The two draggable handles edit the same
// cubic-Bézier control points the engine samples.
public sealed partial class BrightnessCurveCanvas : UserControl
{
    private const int SampleCount = 80;
    private const double PlotPadding = 6.0;
    private const double HandleSize = 24.0;
    private const double HandleRadius = HandleSize / 2.0;

    public BrightnessCurveCanvas()
    {
        InitializeComponent();
        Loaded += (_, _) => RebuildPlot();
        ActualThemeChanged += (_, _) => RebuildPlot();
        Handle1.DragDelta += OnHandle1DragDelta;
        Handle2.DragDelta += OnHandle2DragDelta;
    }

    public static readonly DependencyProperty X1Property =
        DependencyProperty.Register(
            nameof(X1),
            typeof(double),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(0.42, OnAnyVisualChanged));

    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    public static readonly DependencyProperty Y1Property =
        DependencyProperty.Register(
            nameof(Y1),
            typeof(double),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(0.00, OnAnyVisualChanged));

    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    public static readonly DependencyProperty X2Property =
        DependencyProperty.Register(
            nameof(X2),
            typeof(double),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(1.00, OnAnyVisualChanged));

    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    public static readonly DependencyProperty Y2Property =
        DependencyProperty.Register(
            nameof(Y2),
            typeof(double),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(1.00, OnAnyVisualChanged));

    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    public static readonly DependencyProperty MinBrightnessEnabledProperty =
        DependencyProperty.Register(
            nameof(MinBrightnessEnabled),
            typeof(bool),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(true, OnAnyVisualChanged));

    public bool MinBrightnessEnabled
    {
        get => (bool)GetValue(MinBrightnessEnabledProperty);
        set => SetValue(MinBrightnessEnabledProperty, value);
    }

    public static readonly DependencyProperty MinBrightnessProperty =
        DependencyProperty.Register(
            nameof(MinBrightness),
            typeof(int),
            typeof(BrightnessCurveCanvas),
            new PropertyMetadata(180, OnAnyVisualChanged));

    public int MinBrightness
    {
        get => (int)GetValue(MinBrightnessProperty);
        set => SetValue(MinBrightnessProperty, value);
    }

    private static void OnAnyVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BrightnessCurveCanvas self)
            self.RebuildPlot();
    }

    private void OnPlotCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RebuildPlot();

    private void RebuildPlot()
    {
        if (PlotCanvas is null || Handle1 is null || Handle2 is null) return;

        double w = PlotCanvas.ActualWidth;
        double h = PlotCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        PlotCanvas.Children.Clear();

        var g = new PlotRect(w, h);
        if (!g.Valid) return;

        AddReference(g);
        AddFloor(g);
        AddCurve(g);
        AddStems(g);
        RepositionHandles(g);
    }

    private void AddReference(PlotRect g)
    {
        var reference = new Line
        {
            X1 = g.Left,
            Y1 = g.Bottom,
            X2 = g.Right,
            Y2 = g.Top,
            Stroke = ResourceBrush("DividerStrokeColorDefaultBrush"),
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 3.0, 3.0 },
        };
        PlotCanvas.Children.Add(reference);
    }

    private void AddFloor(PlotRect g)
    {
        if (!MinBrightnessEnabled) return;

        double floor = Math.Clamp(MinBrightness / 254.0, 0.0, 1.0);
        double y = g.MapY(floor);
        var line = new Line
        {
            X1 = g.Left,
            Y1 = y,
            X2 = g.Right,
            Y2 = y,
            Stroke = ResourceBrush("SystemFillColorCautionBrush"),
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2.0, 3.0 },
            Opacity = 0.85,
        };
        PlotCanvas.Children.Add(line);
    }

    private void AddCurve(PlotRect g)
    {
        var curve = new Polyline
        {
            Stroke = ResourceBrush("AccentFillColorDefaultBrush"),
            StrokeThickness = 1.75,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var bez = new AmbientBrightnessCurve(X1, Y1, X2, Y2);
        for (int i = 0; i <= SampleCount; i++)
        {
            double x = (double)i / SampleCount;
            double y = bez.Solve(x);
            curve.Points.Add(new Point(g.MapX(x), g.MapY(y)));
        }
        PlotCanvas.Children.Add(curve);
    }

    private void AddStems(PlotRect g)
    {
        var brush = ResourceBrush("AccentFillColorDefaultBrush");
        AddStem(g.MapX(0), g.MapY(0), g.MapX(Clamp01(X1)), g.MapY(Clamp01(Y1)), brush);
        AddStem(g.MapX(1), g.MapY(1), g.MapX(Clamp01(X2)), g.MapY(Clamp01(Y2)), brush);
    }

    private void AddStem(double x1, double y1, double x2, double y2, Brush brush)
    {
        var stem = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 1.0,
            Opacity = 0.35,
        };
        PlotCanvas.Children.Add(stem);
    }

    private void RepositionHandles(PlotRect g)
    {
        Place(Handle1, g.MapX(Clamp01(X1)), g.MapY(Clamp01(Y1)));
        Place(Handle2, g.MapX(Clamp01(X2)), g.MapY(Clamp01(Y2)));
    }

    private static void Place(Thumb t, double cx, double cy)
    {
        Canvas.SetLeft(t, cx - HandleRadius);
        Canvas.SetTop(t, cy - HandleRadius);
    }

    private void OnHandle1DragDelta(object sender, DragDeltaEventArgs e) => DragHandle(Handle1, e, first: true);
    private void OnHandle2DragDelta(object sender, DragDeltaEventArgs e) => DragHandle(Handle2, e, first: false);

    private void DragHandle(Thumb handle, DragDeltaEventArgs e, bool first)
    {
        var g = new PlotRect(PlotCanvas.ActualWidth, PlotCanvas.ActualHeight);
        if (!g.Valid) return;

        double curLeft = Canvas.GetLeft(handle);
        double curTop = Canvas.GetTop(handle);
        if (double.IsNaN(curLeft) || double.IsNaN(curTop))
        {
            RepositionHandles(g);
            return;
        }

        double cx = curLeft + HandleRadius + e.HorizontalChange;
        double cy = curTop + HandleRadius + e.VerticalChange;
        double nx = Math.Round(Math.Clamp((cx - g.Left) / (g.Right - g.Left), 0.0, 1.0), 2);
        double ny = Math.Round(Math.Clamp((g.Bottom - cy) / (g.Bottom - g.Top), 0.0, 1.0), 2);

        if (first)
        {
            X1 = nx;
            Y1 = ny;
        }
        else
        {
            X2 = nx;
            Y2 = ny;
        }
    }

    private Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];

    private static double Clamp01(double value)
        => double.IsNaN(value) || double.IsInfinity(value)
            ? 0.0
            : Math.Clamp(value, 0.0, 1.0);

    private readonly struct PlotRect
    {
        public readonly bool Valid;
        public readonly double Left, Right, Top, Bottom;

        public PlotRect(double w, double h)
        {
            Left = PlotPadding;
            Right = w - PlotPadding;
            Top = PlotPadding;
            Bottom = h - PlotPadding;
            Valid = w > 0 && h > 0 && Right - Left >= 20 && Bottom - Top >= 20;
        }

        public double MapX(double x) => Left + x * (Right - Left);
        public double MapY(double y) => Bottom - y * (Bottom - Top);
    }
}
