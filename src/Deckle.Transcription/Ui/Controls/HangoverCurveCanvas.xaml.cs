using System;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Deckle.Transcription;

// Editor and plot of the dynamic-hangover decay curve the EnergySegmenter
// applies between HangoverRampStart and HangoverRampEnd. X is the open-utterance
// length, Y the decision delay, both in seconds. The two cubic-Bézier control
// points are draggable Thumbs in the normalised [0,1]² curve box, bounded by the
// RampStart/RampEnd guides and the Max/Min delay lines; a drag writes the X1..Y2
// dependency properties, which the host binds two-way.
//
// Drawn in Win2D immediate mode, not as a XAML Shapes tree: rebuilding shapes
// on every SizeChanged frame lags during a window drag, while CanvasControl
// reissues Draw itself on resize. The easing math is shared with the segmenter
// through UnitBezier, so the two need no hand-mirroring.
public sealed partial class HangoverCurveCanvas : UserControl
{
    private const int    SampleCount  = 96;

    private const double GutterLeft   = 46.0;
    private const double GutterBottom = 24.0;
    private const double PadTop       = 12.0;
    private const double PadRight     = 14.0;
    private const double TickLength   = 4.0;

    // Handle Thumb geometry — must match the CurveHandleStyle box in the XAML.
    private const double HandleSize   = 24.0;
    private const double HandleRadius = HandleSize / 2.0;

    // Device-independent: they carry no GPU handle, so they survive device loss.
    private static readonly CanvasTextFormat LabelRight = new()
        { FontSize = 11, HorizontalAlignment = CanvasHorizontalAlignment.Right,  VerticalAlignment = CanvasVerticalAlignment.Center, WordWrapping = CanvasWordWrapping.NoWrap };
    private static readonly CanvasTextFormat LabelCenter = new()
        { FontSize = 11, HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center, WordWrapping = CanvasWordWrapping.NoWrap };
    private static readonly CanvasStrokeStyle GuideDash     = new() { CustomDashStyle = new[] { 2f, 2f } };
    private static readonly CanvasStrokeStyle ReferenceDash = new() { CustomDashStyle = new[] { 3f, 3f } };
    private static readonly CanvasStrokeStyle LiveStroke    = new()
        { LineJoin = CanvasLineJoin.Round, StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    public HangoverCurveCanvas()
    {
        InitializeComponent();
        // Colours are read from the resource dictionary at Draw time, so a theme
        // switch needs a redraw.
        ActualThemeChanged += (_, _) => Plot.Invalidate();
        Handle1.DragDelta += OnHandle1DragDelta;
        Handle2.DragDelta += OnHandle2DragDelta;
    }

    // ── Control-point DPs (the two cubic-Bézier handles, each coord in [0,1]) ────

    public static readonly DependencyProperty X1Property =
        DependencyProperty.Register(nameof(X1), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.42, OnAnyVisualChanged));
    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }

    public static readonly DependencyProperty Y1Property =
        DependencyProperty.Register(nameof(Y1), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.30, OnAnyVisualChanged));
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }

    public static readonly DependencyProperty X2Property =
        DependencyProperty.Register(nameof(X2), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.85, OnAnyVisualChanged));
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }

    public static readonly DependencyProperty Y2Property =
        DependencyProperty.Register(nameof(Y2), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.50, OnAnyVisualChanged));
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    // ── Anchor DPs (seconds) ───────────────────────────────────────────────────

    public static readonly DependencyProperty HangoverMaxSecProperty =
        DependencyProperty.Register(nameof(HangoverMaxSec), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(5.0, OnAnyVisualChanged));
    public double HangoverMaxSec { get => (double)GetValue(HangoverMaxSecProperty); set => SetValue(HangoverMaxSecProperty, value); }

    public static readonly DependencyProperty HangoverMinSecProperty =
        DependencyProperty.Register(nameof(HangoverMinSec), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.5, OnAnyVisualChanged));
    public double HangoverMinSec { get => (double)GetValue(HangoverMinSecProperty); set => SetValue(HangoverMinSecProperty, value); }

    public static readonly DependencyProperty RampStartSecProperty =
        DependencyProperty.Register(nameof(RampStartSec), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(15.0, OnAnyVisualChanged));
    public double RampStartSec { get => (double)GetValue(RampStartSecProperty); set => SetValue(RampStartSecProperty, value); }

    public static readonly DependencyProperty RampEndSecProperty =
        DependencyProperty.Register(nameof(RampEndSec), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(120.0, OnAnyVisualChanged));
    public double RampEndSec { get => (double)GetValue(RampEndSecProperty); set => SetValue(RampEndSecProperty, value); }

    private static void OnAnyVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // DP defaults can resolve before the named parts exist, hence the null
        // checks on Plot and Handle1.
        if (d is not HangoverCurveCanvas self) return;
        self.Plot?.Invalidate();
        if (self.Handle1 is not null) self.RepositionHandles();
    }

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => RepositionHandles();

    // Set by the host while an interactive resize is in flight. A plain bool
    // pushed in from outside keeps this control free of any Deckle.Shell or Win32
    // dependency. While true, OnDraw drops the axis labels, whose per-frame
    // DWrite text layout is what lags during a drag.
    private bool _suspendExpensiveDraw;
    public bool SuspendExpensiveDraw
    {
        get => _suspendExpensiveDraw;
        set
        {
            if (_suspendExpensiveDraw == value) return;
            _suspendExpensiveDraw = value;
            Plot?.Invalidate();
        }
    }

    // Shared by the draw, the handle placement and the drag hit-test so all three
    // agree on where the curve box sits.
    private readonly struct PlotRect
    {
        public readonly bool   Valid;
        public readonly double Left, Right, Top, Bottom;
        public readonly double YMax, XMax;
        public readonly double StartSec, EndSec, MaxSec, MinSec;

        public PlotRect(double w, double h, double maxSec, double minSec, double startSec, double endSec)
        {
            MaxSec   = maxSec;
            MinSec   = minSec;
            StartSec = startSec;
            EndSec   = endSec;

            YMax = Math.Max(maxSec, 1e-3);
            double span = endSec - startSec;
            XMax = endSec + Math.Max(span * 0.15, 5.0);
            if (XMax <= 0) XMax = 10.0;

            Left   = GutterLeft;
            Right  = w - PadRight;
            Top    = PadTop;
            Bottom = h - GutterBottom;
            Valid  = w > 0 && h > 0 && Right - Left >= 20 && Bottom - Top >= 20;
        }

        public double MapX(double xSec) => Left + (xSec / XMax) * (Right - Left);
        public double MapY(double ySec) => Bottom - (ySec / YMax) * (Bottom - Top);

        // Normalised curve-box coordinate ([0,1]²) → plot pixels. (0,0) sits at
        // (RampStart, Max) — top-left of the box — and (1,1) at (RampEnd, Min).
        public double BoxX(double nx) => MapX(StartSec + nx * (EndSec - StartSec));
        public double BoxY(double ny) => MapY(MaxSec - ny * (MaxSec - MinSec));
    }

    private PlotRect Geometry()
    {
        double maxSec   = Clean(HangoverMaxSec, 5.0);
        double minSec   = Clean(HangoverMinSec, 0.5);
        double startSec = Math.Max(0.0, Clean(RampStartSec, 0.0));
        double endSec   = Clean(RampEndSec, startSec);
        if (minSec > maxSec)   minSec = maxSec;
        if (endSec < startSec) endSec = startSec;
        double w = Plot is null ? 0.0 : Plot.Size.Width;
        double h = Plot is null ? 0.0 : Plot.Size.Height;
        return new PlotRect(w, h, maxSec, minSec, startSec, endSec);
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession ds = args.DrawingSession;
        PlotRect g = Geometry();
        if (!g.Valid) return;

        // Axis rulers first, under everything else.
        AddYRuler(ds, g);
        AddXRuler(ds, g);

        AddGuide(ds, g, g.StartSec);
        AddGuide(ds, g, g.EndSec);

        AddCurve(sender, ds, g, asReference: true);
        AddCurve(sender, ds, g, asReference: false);

        AddStems(ds, g);
    }

    // The v = 0 gridline doubles as the X baseline.
    private void AddYRuler(CanvasDrawingSession ds, PlotRect g)
    {
        double step = NiceStep(g.YMax, 4);
        if (step <= 0) return;
        Color grid = WithAlpha(ThemeColor("DividerStrokeColorDefaultBrush"), 0.5);

        for (double v = 0; v <= g.YMax + step * 1e-3; v += step)
        {
            double y = g.MapY(v);
            ds.DrawLine((float)g.Left, (float)y, (float)g.Right, (float)y, grid, 1.0f);
            AddLabel(ds, $"{v:0.#} s", 0, y - 8, GutterLeft - 8, LabelRight);
        }
    }

    // Ticks only, no vertical gridlines: the RampStart / RampEnd guides own the
    // verticals so the plot stays clean.
    private void AddXRuler(CanvasDrawingSession ds, PlotRect g)
    {
        double step = NiceStep(g.XMax, 5);
        if (step <= 0) return;
        Color grid = WithAlpha(ThemeColor("DividerStrokeColorDefaultBrush"), 0.5);

        for (double v = 0; v <= g.XMax + step * 1e-3; v += step)
        {
            double x = g.MapX(v);
            ds.DrawLine((float)x, (float)g.Bottom, (float)x, (float)(g.Bottom + TickLength), grid, 1.0f);
            AddLabel(ds, $"{v:0} s", x - 24, g.Bottom + TickLength + 2, 48, LabelCenter);
        }
    }

    // DWrite lays the string out on every call, so labels are skipped while a
    // resize is in flight; they come back when the size settles.
    private void AddLabel(CanvasDrawingSession ds, string text, double left, double top, double width, CanvasTextFormat format)
    {
        if (_suspendExpensiveDraw) return;
        ds.DrawText(text, new Rect(left, top, width, 16), ThemeColor("TextFillColorTertiaryBrush"), format);
    }

    private static double NiceStep(double range, int targetTicks)
    {
        if (range <= 0 || targetTicks <= 0) return 0;
        double raw  = range / targetTicks;
        double mag  = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double step = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
        return step * mag;
    }

    private void AddGuide(CanvasDrawingSession ds, PlotRect g, double xSec)
    {
        double x = g.MapX(xSec);
        ds.DrawLine((float)x, (float)g.Top, (float)x, (float)g.Bottom,
            ThemeColor("DividerStrokeColorDefaultBrush"), 1.0f, GuideDash);
    }

    private void AddCurve(CanvasControl rc, CanvasDrawingSession ds, PlotRect g, bool asReference)
    {
        var bez = asReference ? default : new UnitBezier(
            Clean(X1, 0.42), Clean(Y1, 0.30), Clean(X2, 0.85), Clean(Y2, 0.50));

        using var path = new CanvasPathBuilder(rc);
        for (int i = 0; i <= SampleCount; i++)
        {
            double xSec = (double)i / SampleCount * g.XMax;
            double ySec;
            if (xSec <= g.StartSec)      ySec = g.MaxSec;
            else if (xSec >= g.EndSec)   ySec = g.MinSec;
            else
            {
                double span = g.EndSec - g.StartSec;
                double p = span <= 0 ? 1.0 : (xSec - g.StartSec) / span;
                double e = asReference ? p : bez.Solve(p);
                ySec = g.MaxSec - (g.MaxSec - g.MinSec) * e;
            }
            float px = (float)g.MapX(xSec);
            float py = (float)g.MapY(ySec);
            if (i == 0) path.BeginFigure(px, py);
            else        path.AddLine(px, py);
        }
        path.EndFigure(CanvasFigureLoop.Open);

        using CanvasGeometry geometry = CanvasGeometry.CreatePath(path);
        Color color = ThemeColor(asReference ? "DividerStrokeColorDefaultBrush" : "AccentFillColorDefaultBrush");
        ds.DrawGeometry(geometry, color, asReference ? 1.0f : 1.5f, asReference ? ReferenceDash : LiveStroke);
    }

    private void AddStems(CanvasDrawingSession ds, PlotRect g)
    {
        Color stem = WithAlpha(ThemeColor("AccentFillColorDefaultBrush"), 0.45);
        ds.DrawLine((float)g.BoxX(0), (float)g.BoxY(0),
                    (float)g.BoxX(Clamp01(X1)), (float)g.BoxY(Clamp01(Y1)), stem, 1.0f);
        ds.DrawLine((float)g.BoxX(1), (float)g.BoxY(1),
                    (float)g.BoxX(Clamp01(X2)), (float)g.BoxY(Clamp01(Y2)), stem, 1.0f);
    }

    private void RepositionHandles()
    {
        PlotRect g = Geometry();
        if (!g.Valid) return;
        Place(Handle1, g.BoxX(Clamp01(X1)), g.BoxY(Clamp01(Y1)));
        Place(Handle2, g.BoxX(Clamp01(X2)), g.BoxY(Clamp01(Y2)));
    }

    private static void Place(Thumb t, double cx, double cy)
    {
        Canvas.SetLeft(t, cx - HandleRadius);
        Canvas.SetTop(t,  cy - HandleRadius);
    }

    private void OnHandle1DragDelta(object sender, DragDeltaEventArgs e) => DragHandle(Handle1, e, first: true);
    private void OnHandle2DragDelta(object sender, DragDeltaEventArgs e) => DragHandle(Handle2, e, first: false);

    private void DragHandle(Thumb t, DragDeltaEventArgs e, bool first)
    {
        PlotRect g = Geometry();
        if (!g.Valid) return;

        double leftPx  = g.BoxX(0), rightPx = g.BoxX(1);
        double topPx   = g.BoxY(0), botPx   = g.BoxY(1);
        if (rightPx - leftPx < 1 || botPx - topPx < 1) return;

        // The Thumb may not have been placed yet on the very first delta — pin it,
        // then let the next delta move it.
        double curLeft = Canvas.GetLeft(t);
        if (double.IsNaN(curLeft)) { RepositionHandles(); return; }

        double cx = curLeft + HandleRadius + e.HorizontalChange;
        double cy = Canvas.GetTop(t) + HandleRadius + e.VerticalChange;
        // Snap to the 0.01 grid the manual-entry boxes step on, so a dragged value
        // reads as 0.42, not 0.4237.
        double nx = Math.Round(Clamp01((cx - leftPx) / (rightPx - leftPx)), 2);
        double ny = Math.Round(Clamp01((cy - topPx)  / (botPx - topPx)), 2);

        if (first) { X1 = nx; Y1 = ny; }
        else       { X2 = nx; Y2 = ny; }
    }

    private static double Clamp01(double v) => Math.Clamp(Clean(v, 0.0), 0.0, 1.0);

    private static double Clean(double v, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

    // Win2D draws take a Color, not a Brush, so dimming bakes the alpha into the
    // colour (WithAlpha) instead of setting an Opacity.
    private static Color ThemeColor(string brushKey)
        => ((SolidColorBrush)Application.Current.Resources[brushKey]).Color;

    private static Color WithAlpha(Color c, double a)
        => Color.FromArgb((byte)(c.A * a), c.R, c.G, c.B);
}
