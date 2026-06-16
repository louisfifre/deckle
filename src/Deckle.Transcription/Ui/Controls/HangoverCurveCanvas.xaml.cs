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

// Editor + plot of the dynamic-hangover decay curve the EnergySegmenter applies
// between HangoverRampStart and HangoverRampEnd. X is the open-utterance length in
// seconds (0 → a little past RampEnd), Y is the decision delay in seconds
// (0 → HangoverMax). Two polylines overlap : a dashed grey reference = the
// straight (linear) decline, and the live accent curve = the cubic-Bézier easing
// set by the two control points (X1,Y1) and (X2,Y2). Faint vertical guides mark
// RampStart and RampEnd.
//
// ── The editor ───────────────────────────────────────────────────────────────
// The two control points are draggable handles (Thumbs) over the plot, the same
// direct-manipulation model as a CSS cubic-bezier editor. The handles live in the
// normalised [0,1]² curve box — the rectangle bounded by the RampStart/RampEnd
// guides (x) and the Max/Min delay lines (y). A drag writes the X1/Y1 or X2/Y2
// dependency property and raises ControlPointsChanged ; the host page reads the
// four values back. Faint stems from (0,0)→P1 and (1,1)→P2 echo the handles.
//
// The control captions its own axes : a faint horizontal grid on Y (delay
// seconds) with left-gutter labels and a tick ruler on X (utterance seconds)
// with bottom-gutter labels, both graduated on "nice" steps.
//
// ── Rendering : Win2D immediate mode ─────────────────────────────────────────
// Everything is drawn procedurally in OnDraw on a CanvasControl (GPU, Direct2D),
// NOT as a XAML Shapes tree. A data or theme change calls Plot.Invalidate(),
// which schedules a single Draw ; CanvasControl reissues Draw itself on resize.
// There is no Children.Clear + element re-creation per SizeChanged frame, which
// is what made the previous retained-mode version lag while the window was
// dragged. The easing math is shared with the segmenter through UnitBezier, so
// the two need no hand-mirroring.
public sealed partial class HangoverCurveCanvas : UserControl
{
    private const int    SampleCount  = 96;

    // Gutters reserved inside the plot for the axis rulers : the left gutter holds
    // the Y labels (delay seconds), the bottom gutter the X labels (utterance
    // seconds). PadTop / PadRight keep the curve off the frame.
    private const double GutterLeft   = 46.0;
    private const double GutterBottom = 24.0;
    private const double PadTop       = 12.0;
    private const double PadRight     = 14.0;
    private const double TickLength   = 4.0;

    // Handle Thumb geometry — must match the CurveHandleStyle box in the XAML.
    private const double HandleSize   = 24.0;
    private const double HandleRadius = HandleSize / 2.0;

    // Device-independent draw resources, built once : safe to hold across device
    // loss (they carry no GPU handle) and shared by every Draw.
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
        // Re-read the theme brushes on a light/dark switch (the colours are pulled
        // from the resource dictionary at Draw time).
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
        // Schedule one Draw and keep the handles pinned to the (possibly shifted)
        // curve box. Guard the handles : DP defaults can resolve before the named
        // parts exist.
        if (d is not HangoverCurveCanvas self) return;
        self.Plot?.Invalidate();
        if (self.Handle1 is not null) self.RepositionHandles();
    }

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => RepositionHandles();

    // ── Resize coalescing hook ───────────────────────────────────────────────────
    //
    // Set true by the host window while an interactive resize gesture is in flight
    // (driven off the Shell's ResizeCoalescer). Kept a plain bool, pushed in from
    // outside, so this control carries no Deckle.Shell or Win32 dependency. While
    // true OnDraw still strokes the curves, grid and guides — cheap GPU line work —
    // but drops the axis labels, whose per-frame DWrite text layout is the cost
    // that made the drag lag. Toggling it schedules one Draw: the light pass on the
    // way in, the full pass (labels back) on settle.
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

    // ── PlotRect geometry ────────────────────────────────────────────────────────────
    //
    // The pixel frame of the plot, derived once from the current size and the
    // anchor values, then shared by the draw, the handle placement and the drag
    // hit-test so all three agree on where the curve box sits.
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

    // ── Draw ─────────────────────────────────────────────────────────────────

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDrawingSession ds = args.DrawingSession;
        PlotRect g = Geometry();
        if (!g.Valid) return;

        // Axis rulers first (under everything else).
        AddYRuler(ds, g);
        AddXRuler(ds, g);

        // Faint vertical guides at RampStart / RampEnd — the bounds of the decline.
        AddGuide(ds, g, g.StartSec);
        AddGuide(ds, g, g.EndSec);

        // Dashed reference = straight (linear) decline, then the live Bézier curve.
        AddCurve(sender, ds, g, asReference: true);
        AddCurve(sender, ds, g, asReference: false);

        // Handle stems : (0,0)→P1 and (1,1)→P2, faint accent lines under the Thumbs.
        AddStems(ds, g);
    }

    // ── Axis rulers ──────────────────────────────────────────────────────────────

    // Y ruler : faint full-width horizontal gridlines on "nice" delay steps, each
    // labelled in the left gutter. The v = 0 line doubles as the X baseline.
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

    // X ruler : short tick marks hanging under the baseline on "nice" utterance
    // steps, each labelled in the bottom gutter. No full vertical gridlines — the
    // RampStart / RampEnd dashed guides own the verticals so the plot stays clean.
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

    // Axis label, drawn into a fixed box so the text format's alignment lands it
    // (right-aligned in the left gutter, centred under each X tick). DWrite lays the
    // string out on every call — the per-frame cost — so it is skipped while a
    // resize gesture coalesces; the labels snap back when the size settles.
    private void AddLabel(CanvasDrawingSession ds, string text, double left, double top, double width, CanvasTextFormat format)
    {
        if (_suspendExpensiveDraw) return;
        ds.DrawText(text, new Rect(left, top, width, 16), ThemeColor("TextFillColorTertiaryBrush"), format);
    }

    // "Nice" tick step (1 / 2 / 5 × 10ⁿ) so the ruler lands on round seconds
    // regardless of the current ramp bounds.
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

    // The two handle stems : faint accent lines from each fixed endpoint to its
    // control point, the visual tie between the corner and its handle.
    private void AddStems(CanvasDrawingSession ds, PlotRect g)
    {
        Color stem = WithAlpha(ThemeColor("AccentFillColorDefaultBrush"), 0.45);
        ds.DrawLine((float)g.BoxX(0), (float)g.BoxY(0),
                    (float)g.BoxX(Clamp01(X1)), (float)g.BoxY(Clamp01(Y1)), stem, 1.0f);
        ds.DrawLine((float)g.BoxX(1), (float)g.BoxY(1),
                    (float)g.BoxX(Clamp01(X2)), (float)g.BoxY(Clamp01(Y2)), stem, 1.0f);
    }

    // ── Handles (the curve editor) ───────────────────────────────────────────────

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
        // reads as 0.42, not 0.4237 — 100 steps across the box stays smooth.
        double nx = Math.Round(Clamp01((cx - leftPx) / (rightPx - leftPx)), 2);
        double ny = Math.Round(Clamp01((cy - topPx)  / (botPx - topPx)), 2);

        // Setting the DP propagates to the bound view-model (the X1..Y2 DPs are
        // bound two-way by the host), which drives the readouts and the box entry.
        if (first) { X1 = nx; Y1 = ny; }
        else       { X2 = nx; Y2 = ny; }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static double Clamp01(double v) => Math.Clamp(Clean(v, 0.0), 0.0, 1.0);

    private static double Clean(double v, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

    // Theme brush colour pulled from the merged resource dictionary, optionally
    // dimmed by baking the alpha into the colour (Win2D draws take a Color, not a
    // Brush, so there is no Opacity to set on the line).
    private static Color ThemeColor(string brushKey)
        => ((SolidColorBrush)Application.Current.Resources[brushKey]).Color;

    private static Color WithAlpha(Color c, double a)
        => Color.FromArgb((byte)(c.A * a), c.R, c.G, c.B);
}
