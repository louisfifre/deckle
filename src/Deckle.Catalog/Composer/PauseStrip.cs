using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Deckle.Catalog;

// The words of the pause strip, resolved by its owner from the module's
// strings: the caption over the strip, the four labels drawn on it, and the
// accessible names of its two handles (the fields' captions).
public sealed record PauseStripText(
    string Caption,
    string MinimumSpeech,
    string Kept,
    string Dropped,
    string Cut,
    string MinimumSpeechName,
    string MarginName);

// ── PauseStrip ────────────────────────────────────────────────────────────────
//
// One pause, zoomed: the end of an utterance at the scale of a word, under the
// cut curve whose axis (0 to ~140 s) is too coarse for it. A voiced burst, then
// the pause after it: the part kept with the utterance (the cut margin), the
// part dropped, and the cut, placed at the shortest pause that cuts (the
// curve's minimum wait, read here, moved on the curve). A bracket over the
// burst's start is the minimum speech: a burst shorter than it is a blip, and
// the burst greys when the bracket is dragged past its end. A ruler in seconds
// under the lane says the zoom, so the strip never passes for the curve's axis.
//
// Two handles, the minimum speech's and the margin's, the bracket's and the
// kept band's right edges, drag horizontally; the scale (PauseScale) is frozen
// while one is in flight and recomputed at the release, so nothing moves under
// the pointer. A handle writes at its release and at each arrow key (a step of
// its grain, ten with Shift), the gesture rule. The lane starts and ends where
// the curve's data area does (CurveEditor's gutters), so the two views share
// their edges. Shapes in place, theme brushes, like the curve.
public sealed partial class PauseStrip : UserControl
{
    private const double LabelTop = 0.0;
    private const double LaneTop = 18.0;
    private const double LaneHeight = 22.0;
    private const double RulerTop = LaneTop + LaneHeight + 6.0;
    private const double CanvasHeight = RulerTop + 18.0;
    private const double HandleWidth = 16.0;
    private const double HandleHeight = LaneHeight + 10.0;
    private const double LabelGap = 6.0;

    private readonly Canvas _lane = new() { Height = CanvasHeight };
    private readonly Rectangle _burst = new();
    private readonly Rectangle _bracket = new() { StrokeThickness = 1.5 };
    private readonly Rectangle _kept = new();
    private readonly Line _dropped = new() { StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2.0, 3.0 } };
    private readonly Line _cut = new() { StrokeThickness = 1.5 };
    private readonly TextBlock _caption;
    private readonly TextBlock _minimumLabel, _keptLabel, _droppedLabel, _cutLabel;
    private readonly Thumb _minimumHandle, _marginHandle;
    private readonly List<FrameworkElement> _ruler = new();

    private double _minimumSpeech = 0.25, _margin = 0.15, _cutAfter = 0.5;
    private PauseScale _scale = PauseScale.For(0.25, 0.15, 0.5);
    private bool _dragging;
    private double _dragPixel;

    public PauseStrip(PauseStripText text)
    {
        Style? caption = Application.Current.Resources["CaptionTextBlockStyle"] as Style;
        _caption = new TextBlock { Text = text.Caption, Style = caption, Margin = new Thickness(0, 0, 0, 4) };
        _minimumLabel = Label(text.MinimumSpeech);
        _keptLabel = Label(text.Kept);
        _droppedLabel = Label(text.Dropped);
        _cutLabel = Label(text.Cut);

        _minimumHandle = BuildHandle(minimum: true);
        _marginHandle = BuildHandle(minimum: false);
        AutomationProperties.SetName(_minimumHandle, text.MinimumSpeechName);
        AutomationProperties.SetName(_marginHandle, text.MarginName);

        _lane.Children.Add(_burst);
        _lane.Children.Add(_kept);
        _lane.Children.Add(_dropped);
        _lane.Children.Add(_cut);
        _lane.Children.Add(_bracket);
        _lane.Children.Add(_minimumLabel);
        _lane.Children.Add(_keptLabel);
        _lane.Children.Add(_droppedLabel);
        _lane.Children.Add(_cutLabel);
        _lane.Children.Add(_minimumHandle);
        _lane.Children.Add(_marginHandle);
        _lane.SizeChanged += (_, _) => Redraw();

        var stack = new StackPanel();
        stack.Children.Add(_caption);
        stack.Children.Add(_lane);
        Content = stack;

        ActualThemeChanged += (_, _) => ApplyBrushes();
        Loaded += (_, _) => ApplyBrushes();

        static TextBlock Label(string value) => new()
        {
            Text = value,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.NoWrap,
        };
    }

    // ── Values ───────────────────────────────────────────────────────────────

    // The three durations, in seconds. Set by the owner on every refresh; the
    // scale follows them unless a handle is in flight.
    public double MinimumSpeech { get => _minimumSpeech; set { _minimumSpeech = value; Rescale(); } }
    public double CutMargin { get => _margin; set { _margin = value; Rescale(); } }
    public double CutAfter { get => _cutAfter; set { _cutAfter = value; Rescale(); } }

    // The bounds and grain of the two handles, the ones their fields take.
    public AxisLimit MinimumSpeechLimit { get; set; } = new(0, 1, 0.05);
    public AxisLimit CutMarginLimit { get; set; } = new(0, 1, 0.05);

    // Raised at a handle's release or arrow key, with the value it asks for.
    public event EventHandler<double>? MinimumSpeechCommitted;
    public event EventHandler<double>? CutMarginCommitted;

    // The lane's horizontal insets, so it shares the curve's data edges.
    public double LeftInset { get; set; }
    public double RightInset { get; set; }

    private void Rescale()
    {
        if (!_dragging) _scale = PauseScale.For(_minimumSpeech, _margin, _cutAfter);
        Redraw();
    }

    // ── Handles ──────────────────────────────────────────────────────────────

    private Thumb BuildHandle(bool minimum)
    {
        var thumb = new Thumb
        {
            Width = HandleWidth,
            Height = HandleHeight,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            IsTabStop = true,
            UseSystemFocusVisuals = true,
        };
        // A vertical grip, the accent bar with the base-background ring the
        // curve's handles wear, in a 16 × 32 target: an edge that slides.
        const string template =
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Thumb'>" +
            "<Grid Background='Transparent'><Rectangle Width='6' Height='24' RadiusX='3' RadiusY='3' " +
            "HorizontalAlignment='Center' VerticalAlignment='Center' " +
            "Fill='{ThemeResource AccentFillColorDefaultBrush}' Stroke='{ThemeResource SolidBackgroundFillColorBaseBrush}' StrokeThickness='1.5' />" +
            "</Grid></ControlTemplate>";
        thumb.Template = (ControlTemplate)XamlReader.Load(template);

        thumb.DragStarted += (_, _) =>
        {
            _dragging = true;
            _dragPixel = MapX(minimum ? _minimumSpeech : _scale.SpeechEnd + _margin);
        };
        thumb.DragDelta += (_, e) =>
        {
            _dragPixel += e.HorizontalChange;
            double seconds = _scale.Seconds(UnmapX(_dragPixel));
            if (minimum) _minimumSpeech = MinimumSpeechLimit.Apply(seconds);
            else _margin = CutMarginLimit.Apply(seconds - _scale.SpeechEnd);
            Redraw();
        };
        thumb.DragCompleted += (_, _) =>
        {
            _dragging = false;
            if (minimum) MinimumSpeechCommitted?.Invoke(this, _minimumSpeech);
            else CutMarginCommitted?.Invoke(this, _margin);
            Rescale();
        };
        thumb.KeyDown += (_, e) => OnHandleKeyDown(minimum, e);
        return thumb;
    }

    private void OnHandleKeyDown(bool minimum, KeyRoutedEventArgs e)
    {
        int direction = e.Key switch { VirtualKey.Left => -1, VirtualKey.Right => 1, _ => 0 };
        if (direction == 0) return;
        e.Handled = true;
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                      & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        int steps = direction * (shift ? 10 : 1);
        if (minimum)
        {
            double value = MinimumSpeechLimit.Apply(_minimumSpeech + steps * MinimumSpeechLimit.Step);
            if (value != _minimumSpeech) MinimumSpeechCommitted?.Invoke(this, value);
        }
        else
        {
            double value = CutMarginLimit.Apply(_margin + steps * CutMarginLimit.Step);
            if (value != _margin) CutMarginCommitted?.Invoke(this, value);
        }
    }

    // ── Drawing ──────────────────────────────────────────────────────────────

    private double LaneWidth => Math.Max(0, _lane.ActualWidth - LeftInset - RightInset);
    private double MapX(double seconds) => LeftInset + _scale.Fraction(seconds) * LaneWidth;
    private double UnmapX(double pixel) => LaneWidth <= 0 ? 0 : Math.Clamp((pixel - LeftInset) / LaneWidth, 0, 1);

    private void Redraw()
    {
        if (_lane.ActualWidth <= 0 || LaneWidth < 40) return;

        double speechEnd = _scale.SpeechEnd;
        double x0 = MapX(0), xs = MapX(speechEnd);
        double xKept = MapX(speechEnd + _margin);
        double xCut = MapX(speechEnd + _cutAfter);
        double xMin = MapX(_minimumSpeech);

        Rect(_burst, x0, xs, LaneTop + 4, LaneHeight - 8);
        _burst.Opacity = _scale.IsBlip(_minimumSpeech) ? 0.35 : 1.0;
        Rect(_bracket, x0, xMin, LaneTop, LaneHeight);
        Rect(_kept, xs, xKept, LaneTop + 4, LaneHeight - 8);
        double mid = LaneTop + LaneHeight / 2;
        _dropped.X1 = Math.Max(xKept, xs); _dropped.Y1 = mid;
        _dropped.X2 = Math.Max(xCut, _dropped.X1); _dropped.Y2 = mid;
        _cut.X1 = xCut; _cut.Y1 = LaneTop - 2; _cut.X2 = xCut; _cut.Y2 = LaneTop + LaneHeight + 2;

        // Each label centred on what it names, hidden when its span is too
        // narrow to hold it rather than spilling onto the next.
        PlaceLabel(_minimumLabel, x0, xMin, alignLeft: true);
        PlaceLabel(_keptLabel, xs, xKept, alignLeft: false);
        PlaceLabel(_droppedLabel, xKept, xCut, alignLeft: false);
        PlaceAt(_cutLabel, xCut);

        Canvas.SetLeft(_minimumHandle, xMin - HandleWidth / 2);
        Canvas.SetTop(_minimumHandle, LaneTop + LaneHeight / 2 - HandleHeight / 2);
        Canvas.SetLeft(_marginHandle, xKept - HandleWidth / 2);
        Canvas.SetTop(_marginHandle, LaneTop + LaneHeight / 2 - HandleHeight / 2);
        AutomationProperties.SetItemStatus(_minimumHandle, $"{_minimumSpeech:0.##} s");
        AutomationProperties.SetItemStatus(_marginHandle, $"{_margin:0.##} s");

        DrawRuler();
    }

    private static void Rect(Rectangle rect, double from, double to, double top, double height)
    {
        Canvas.SetLeft(rect, Math.Min(from, to));
        Canvas.SetTop(rect, top);
        rect.Width = Math.Abs(to - from);
        rect.Height = height;
    }

    private static void PlaceLabel(TextBlock label, double from, double to, bool alignLeft)
    {
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = label.DesiredSize.Width;
        double span = to - from;
        if (span < width + LabelGap)
        {
            label.Visibility = Visibility.Collapsed;
            return;
        }
        label.Visibility = Visibility.Visible;
        Canvas.SetLeft(label, alignLeft ? from : from + (span - width) / 2);
        Canvas.SetTop(label, LabelTop);
    }

    private void PlaceAt(TextBlock label, double x)
    {
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = label.DesiredSize.Width;
        double left = Math.Min(x - width / 2, _lane.ActualWidth - width);
        Canvas.SetLeft(label, Math.Max(0, left));
        Canvas.SetTop(label, LabelTop);
    }

    // The zoom's ruler: a baseline under the lane, ticks on the scale's round
    // step, labels in seconds.
    private void DrawRuler()
    {
        foreach (FrameworkElement element in _ruler) _lane.Children.Remove(element);
        _ruler.Clear();
        if (_scale.TickStep <= 0) return;

        Brush grid = Brush("DividerStrokeColorDefaultBrush");
        Brush text = Brush("TextFillColorTertiaryBrush");
        Style? caption = Application.Current.Resources["CaptionTextBlockStyle"] as Style;
        AddRuler(new Line { X1 = MapX(0), Y1 = RulerTop, X2 = MapX(_scale.Span), Y2 = RulerTop, Stroke = grid, StrokeThickness = 1, Opacity = 0.5 });
        for (double v = 0; v <= _scale.Span + _scale.TickStep * 1e-3; v += _scale.TickStep)
        {
            double x = MapX(v);
            AddRuler(new Line { X1 = x, Y1 = RulerTop, X2 = x, Y2 = RulerTop + 4, Stroke = grid, StrokeThickness = 1, Opacity = 0.5 });
            var label = new TextBlock
            {
                Text = $"{v:0.##} s",
                Style = caption,
                Foreground = text,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Width = 48,
            };
            Canvas.SetLeft(label, x - 24);
            Canvas.SetTop(label, RulerTop + 4);
            AddRuler(label);
        }
    }

    private void AddRuler(FrameworkElement element)
    {
        _lane.Children.Insert(0, element);
        _ruler.Add(element);
    }

    private void ApplyBrushes()
    {
        // The voice in the accent, the kept part of the pause in a quieter
        // accent, the dropped part as a dashed divider line, the cut in the
        // primary text colour: one role, one style.
        _burst.Fill = Brush("AccentFillColorDefaultBrush");
        _kept.Fill = Brush("AccentFillColorSecondaryBrush");
        _kept.Opacity = 0.55;
        _bracket.Stroke = Brush("TextFillColorSecondaryBrush");
        _bracket.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _bracket.RadiusX = 3; _bracket.RadiusY = 3;
        _dropped.Stroke = Brush("TextFillColorTertiaryBrush");
        _cut.Stroke = Brush("TextFillColorPrimaryBrush");
        _caption.Foreground = Brush("TextFillColorSecondaryBrush");
        foreach (TextBlock label in new[] { _minimumLabel, _keptLabel, _droppedLabel })
            label.Foreground = Brush("TextFillColorSecondaryBrush");
        _cutLabel.Foreground = Brush("TextFillColorPrimaryBrush");
        Redraw();
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
