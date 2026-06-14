using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Deckle.Transcription;

// Wide diagnostic widget that plots the dynamic-hangover decay curve the
// EnergySegmenter applies between HangoverRampStart and HangoverRampEnd. X is the
// open-utterance length in seconds (0 → a little past RampEnd), Y is the decision
// delay in seconds (0 → HangoverMax). Two polylines overlap : a dashed grey
// reference = the straight (contrast = 1) decline, and the live accent curve
// recomputed whenever any of the seven shape / anchor dependency properties
// changes. Faint vertical guides mark RampStart and RampEnd.
//
// The plot has three regions, mirroring the segmenter : a flat HangoverMax
// shoulder up to RampStart, the shaped decline to RampEnd, then a flat
// HangoverMin floor. No axis labels — the consuming page captions the axes
// around the widget.
//
// The curve math (Ease / RawIntegral / Softplus) is a verbatim copy of
// EnergySegmenter's : the control is a pure visualisation and intentionally does
// not depend on the segmenter internals, so the two MUST be kept in lockstep by
// hand. (Same trade-off BrightnessCurveCanvas takes with AmbientEngine.)
public sealed partial class HangoverCurveCanvas : UserControl
{
    private const int    SampleCount = 96;
    private const double PlotPadding = 6.0;

    public HangoverCurveCanvas()
    {
        InitializeComponent();
        Loaded                 += (_, _) => RebuildCurve();
        PlotBorder.SizeChanged += (_, _) => RebuildCurve();
    }

    // ── Shape DPs ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty ContrastProperty =
        DependencyProperty.Register(nameof(Contrast), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(3.0, OnAnyVisualChanged));
    public double Contrast { get => (double)GetValue(ContrastProperty); set => SetValue(ContrastProperty, value); }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(0.8, OnAnyVisualChanged));
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public static readonly DependencyProperty SharpnessProperty =
        DependencyProperty.Register(nameof(Sharpness), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(20.0, OnAnyVisualChanged));
    public double Sharpness { get => (double)GetValue(SharpnessProperty); set => SetValue(SharpnessProperty, value); }

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
            new PropertyMetadata(60.0, OnAnyVisualChanged));
    public double RampStartSec { get => (double)GetValue(RampStartSecProperty); set => SetValue(RampStartSecProperty, value); }

    public static readonly DependencyProperty RampEndSecProperty =
        DependencyProperty.Register(nameof(RampEndSec), typeof(double), typeof(HangoverCurveCanvas),
            new PropertyMetadata(180.0, OnAnyVisualChanged));
    public double RampEndSec { get => (double)GetValue(RampEndSecProperty); set => SetValue(RampEndSecProperty, value); }

    private static void OnAnyVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HangoverCurveCanvas self) self.RebuildCurve();
    }

    private void RebuildCurve()
    {
        // A Canvas does not stretch to fill its parent (it measures to its
        // content, so its own ActualWidth stays 0). Take the size from the Border,
        // which DOES stretch into the page's layout slot, and size the Canvas
        // explicitly to match so the plot neither stays blank nor clips. Before
        // the first layout pass the Border is still 0 — bail and let SizeChanged
        // re-trigger.
        double w = PlotBorder.ActualWidth  - PlotBorder.BorderThickness.Left - PlotBorder.BorderThickness.Right;
        double h = PlotBorder.ActualHeight - PlotBorder.BorderThickness.Top  - PlotBorder.BorderThickness.Bottom;
        if (w <= 0 || h <= 0) return;

        PlotCanvas.Width  = w;
        PlotCanvas.Height = h;
        PlotCanvas.Children.Clear();

        // Defensive reads — a misconfigured caller (NaN, inverted bounds) must not
        // throw. Keep the curve monotone non-increasing, as the segmenter does.
        double maxSec   = Clean(HangoverMaxSec, 5.0);
        double minSec   = Clean(HangoverMinSec, 0.5);
        double startSec = Math.Max(0.0, Clean(RampStartSec, 0.0));
        double endSec   = Clean(RampEndSec, startSec);
        if (minSec > maxSec)   minSec = maxSec;
        if (endSec < startSec) endSec = startSec;

        double yMax = Math.Max(maxSec, 1e-3);
        double span = endSec - startSec;
        double xMax = endSec + Math.Max(span * 0.15, 5.0);
        if (xMax <= 0) xMax = 10.0;

        // Faint vertical guides at RampStart / RampEnd — the bounds of the decline.
        AddGuide(startSec, xMax, w, h);
        AddGuide(endSec, xMax, w, h);

        // Dashed reference = straight (contrast = 1) decline, then the live curve.
        AddCurve(asReference: true,  maxSec, minSec, startSec, endSec, yMax, xMax, w, h);
        AddCurve(asReference: false, maxSec, minSec, startSec, endSec, yMax, xMax, w, h);
    }

    private void AddGuide(double xSec, double xMax, double w, double h)
    {
        double x = PlotPadding + (xSec / xMax) * (w - 2 * PlotPadding);
        PlotCanvas.Children.Add(new Line
        {
            Stroke          = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2.0, 2.0 },
            X1 = x, Y1 = PlotPadding,
            X2 = x, Y2 = h - PlotPadding,
        });
    }

    private void AddCurve(bool asReference, double maxSec, double minSec,
                          double startSec, double endSec, double yMax, double xMax, double w, double h)
    {
        double c = asReference ? 1.0 : Clean(Contrast, 1.0);
        double m = Clean(Position, 0.8);
        double s = Clean(Sharpness, 20.0);

        var poly = new Polyline
        {
            Stroke = asReference
                ? (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"]
                : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            StrokeThickness    = asReference ? 1.0 : 1.5,
            StrokeLineJoin     = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round,
        };
        if (asReference)
            poly.StrokeDashArray = new DoubleCollection { 3.0, 3.0 };

        for (int i = 0; i <= SampleCount; i++)
        {
            double xSec = (double)i / SampleCount * xMax;
            double ySec = HangoverSecAt(xSec, maxSec, minSec, startSec, endSec, m, c, s);
            double x = PlotPadding + (xSec / xMax) * (w - 2 * PlotPadding);
            double y = (h - PlotPadding) - (ySec / yMax) * (h - 2 * PlotPadding);
            poly.Points.Add(new Point(x, y));
        }
        PlotCanvas.Children.Add(poly);
    }

    // Hangover delay (seconds) for an open utterance of xSec seconds, with the
    // shoulders and the shaped decline — mirrors EnergySegmenter.RequiredHangoverFrames.
    private static double HangoverSecAt(double xSec, double maxSec, double minSec,
                                        double startSec, double endSec, double m, double c, double s)
    {
        if (xSec <= startSec) return maxSec;
        if (xSec >= endSec)   return minSec;
        double denom = endSec - startSec;
        if (denom <= 0) return minSec;
        double p = (xSec - startSec) / denom;
        return maxSec - (maxSec - minSec) * Ease(p, m, c, s);
    }

    private static double Clean(double v, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

    // ── Curve math — verbatim mirror of EnergySegmenter (keep in lockstep) ──────

    private static double Ease(double p, double m, double c, double s)
    {
        if (s < 0.05 || Math.Abs(c - 1.0) < 1e-3) return p;
        double den = RawIntegral(1.0, m, c, s);
        return den <= 0.0 ? p : RawIntegral(p, m, c, s) / den;
    }

    private static double RawIntegral(double p, double m, double c, double s)
        => p + (c - 1.0) * (Softplus(s * (p - m)) - Softplus(-s * m)) / s;

    private static double Softplus(double z)
        => z > 30.0 ? z : Math.Log(1.0 + Math.Exp(z));
}
