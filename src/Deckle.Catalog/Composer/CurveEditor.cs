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
// The one curve component of the settings surfaces: a plot of a two-point
// cubic Bézier with its two draggable control points, taking the width it is
// given, and a legend: the frame's own values when the owner fills the Legend
// slot (the cut's four anchors), then the four control points as fields in a
// two-by-two grid with the `cubic-bezier(…)` readout under them, then the
// owner's LegendAfter values and its LegendTrailing slot (the reset).
//
// Two layouts from one frame (CurveFrame). With axes, the plot spans the data
// space (rulers on "nice" steps, unit labels, dashed guides at the box's edges),
// the unit square sits where the frame puts it — the dictation's decay from the
// ramp start to the ramp end — and the plot is the instrument: full width, its
// height following the page when ViewportShare is set, an optional PlotFooter
// view under it at another scale (the pause strip), and the legend under both,
// its blocks in one row while they fit. With EndpointLimits the square's two
// fixed corners get handles of their own that move the frame (the endpoints
// partial). Without axes, the plot is the unit square alone, square, the
// Ambient brightness response, with the legend beside it, or under it below a
// comfortable width. A dashed reference shows the straight line between the
// fixed endpoints, two faint stems tie each endpoint to its control point, and
// an optional floor guide draws a level the engine holds.
//
// Writes follow the gesture rule: a handle moves the drawing while it is dragged
// and writes at its release; a field writes at its commit; an arrow key on a
// focused handle nudges by a step and writes at once. X stays in [0,1] so the
// curve remains a function; Y may overshoot by a quarter when AllowOutside is
// set. The plot has no fill or frame of its own: it floats on its host's
// surface, its y labels flush with the host's text edge. The drawing is a tree
// of XAML shapes updated in place on every change, with no Win2D.
public sealed partial class CurveEditor : UserControl
{
    private const int SampleCount = 96;
    private const double HandleSize = 24.0;
    private const double HandleRadius = HandleSize / 2.0;
    // The plot floats on its host's surface: no fill, no frame, and no margin
    // beyond what its own labels need. The gutter is the y labels' column (they
    // start flush at the control's left edge, the edge of the card's text); the
    // top pad is half a label's height so the top label starts at the edge, the
    // bottom gutter is the x tick plus its label, and the right pad is half the
    // last x label's width, so the labels' ink is what fills the box.
    private const double GutterLeft = 36.0;
    private const double GutterBottom = 24.0;
    private const double PadTop = 8.0;
    private const double PadRight = 14.0;
    private const double Pad = 0.0;

    // Where an axes plot's data area starts and ends inside the editor, so a
    // PlotFooter view at another scale can share its edges.
    public const double DataLeftInset = GutterLeft;
    public const double DataRightInset = PadRight;
    private const double SquareSide = 220.0;
    private const double StackBelowWidth = 560.0;
    private const double AxisLegendWidth = 120.0;
    private const double Outside = 0.25;

    // Every field of the legend, the owner's frame values and the four control
    // points alike, takes this one width, in columns of this one width, at this
    // one spacing (FieldGrid). Two widths and two layouts had put the second
    // column 16 px apart from one block to the next (2026-11-07 review). 112
    // fits a three-digit value with the compact spin buttons.
    public const double FieldWidth = 112.0;
    private const double FieldColumnGap = 12.0;
    private const double FieldRowGap = 8.0;

    // The gap between the plot and the legend when the legend moves under it.
    private const double StackedRowGap = 12.0;

    // The legend's blocks (the owner's frame values, the control points, the
    // owner's values after them): a column gap between blocks laid side by side
    // under an axes plot, the stacked gap when they no longer fit one row.
    private const double BlockColumnGap = 32.0;
    private const double BlockRowGap = 16.0;

    // The plot's height when it follows the page (ViewportShare): never so low
    // that the rulers crowd the curve, never so tall that the values under it
    // leave the window on a large screen.
    private const double MinShareHeight = 240.0;
    private const double MaxShareHeight = 560.0;

    private readonly Grid _root = new();
    private readonly Grid _side = new();
    private readonly StackPanel _legend = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly StackPanel _legendAfter = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly StackPanel _trailing = new() { Orientation = Orientation.Horizontal, Spacing = 4, Visibility = Visibility.Collapsed };
    private readonly Border _footer = new() { Visibility = Visibility.Collapsed };
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
    private readonly StackPanel _fields = new();
    private readonly Grid _pointsRow = new();
    private readonly StackPanel _axisLegend = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
    private readonly List<TextBlock> _legendLetters = new();
    private readonly TextBlock _xLegendName = new();
    private readonly TextBlock _yLegendName = new();
    private readonly FrameworkElement _xLegendItem, _yLegendItem;
    private readonly NumberBox _x1Box, _y1Box, _x2Box, _y2Box;
    private readonly TextBlock _readout;

    // The handle positions while a drag is in flight, in unit coordinates; the
    // dependency properties are written at the release. The pointer's own
    // position is kept unsnapped (_dragPixel), so a slow drag whose every step
    // is smaller than a hundredth still adds up instead of snapping back.
    private double _dragX1, _dragY1, _dragX2, _dragY2;
    private Point _dragPixel;
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
        _originHandle = BuildEndpointHandle(origin: true);
        _endHandle = BuildEndpointHandle(origin: false);

        _plot.Children.Add(_reference);
        _plot.Children.Add(_guideStart);
        _plot.Children.Add(_guideEnd);
        _plot.Children.Add(_floor);
        _plot.Children.Add(_curve);
        _plot.Children.Add(_stem1);
        _plot.Children.Add(_stem2);
        // The endpoints under the control points: where both meet, the smaller,
        // off-curve handle stays the one on top.
        _plot.Children.Add(_originHandle);
        _plot.Children.Add(_endHandle);
        _plot.Children.Add(_handle1);
        _plot.Children.Add(_handle2);
        _plot.SizeChanged += (_, _) => Redraw();

        // The plot's host: a bare sizing box, with no brush, so the drawing floats
        // on whatever surface (a settings card, a page) carries the editor.
        _plotFrame = new Border
        {
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

        // The axis legend: what x and y mean, in a small column to the right of
        // the control points. Empty until the owner names an axis.
        _axisLegend.Width = AxisLegendWidth;
        _axisLegend.Visibility = Visibility.Collapsed;
        _xLegendItem = BuildLegendItem("x", _xLegendName);
        _yLegendItem = BuildLegendItem("y", _yLegendName);
        _axisLegend.Children.Add(_xLegendItem);
        _axisLegend.Children.Add(_yLegendItem);
        _pointsRow.ColumnSpacing = 24;
        _pointsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pointsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_axisLegend, 1);
        _pointsRow.Children.Add(_fields);
        _pointsRow.Children.Add(_axisLegend);

        // The legend: the owner's frame values first, the control points (and
        // the axis legend beside them), the owner's values after them, then the
        // trailing slot (the composer's reset). Reflow lays them out.
        _side.Children.Add(_legend);
        _side.Children.Add(_pointsRow);
        _side.Children.Add(_legendAfter);
        _side.Children.Add(_trailing);

        // Rows and columns are Reflow's: it declares only those that hold a
        // visible element, since a WinUI Grid pays its spacing even before an
        // empty row (the 12 px of void the 2026-11-07 review measured under the
        // plot).
        _root.ColumnSpacing = 16;
        _root.RowSpacing = StackedRowGap;
        _root.Children.Add(_plotFrame);
        _root.Children.Add(_footer);
        _root.Children.Add(_side);
        Content = _root;

        SizeChanged += (_, e) => Reflow(e.NewSize.Width);
        ActualThemeChanged += (_, _) => ApplyBrushes();
        Loaded += (_, _) =>
        {
            AttachViewport();
            ApplyBrushes(); Reflow(ActualWidth); Redraw(); SyncFields();
        };
        Unloaded += (_, _) => DetachViewport();
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

    public static readonly DependencyProperty XAxisLegendProperty = DependencyProperty.Register(
        nameof(XAxisLegend), typeof(string), typeof(CurveEditor),
        new PropertyMetadata(null, (d, _) => ((CurveEditor)d).ApplyAxisLegend()));
    public static readonly DependencyProperty YAxisLegendProperty = DependencyProperty.Register(
        nameof(YAxisLegend), typeof(string), typeof(CurveEditor),
        new PropertyMetadata(null, (d, _) => ((CurveEditor)d).ApplyAxisLegend()));

    // What each axis stands for, in a few words ("Utterance length"). Shown as a
    // small legend beside the control points, never on the plot; an unset one
    // draws nothing, and with both unset the legend takes no room.
    public string? XAxisLegend
    {
        get => (string?)GetValue(XAxisLegendProperty);
        set => SetValue(XAxisLegendProperty, value);
    }

    public string? YAxisLegend
    {
        get => (string?)GetValue(YAxisLegendProperty);
        set => SetValue(YAxisLegendProperty, value);
    }

    private double _wideHeight = 280.0;

    // The plot's height when the frame carries axes. 280 is the Playground's;
    // the Settings composer raises it to 320, where the plot is the
    // segmentation's main instrument at full card width (2026-10-14). A plain
    // property, set once by the owner after construction: nothing binds it.
    public double WideHeight
    {
        get => _wideHeight;
        set
        {
            _wideHeight = value;
            ApplyPlotHeight();
        }
    }

    private double _viewportShare;
    private ScrollViewer? _viewport;

    // When above zero, the axes plot's height follows the page instead of
    // WideHeight: this share of the visible height of the nearest ScrollViewer
    // around the editor, between MinShareHeight and MaxShareHeight, so the plot
    // takes most of the window on the page where it is the instrument. Zero
    // keeps WideHeight. A plain property, set once by the owner.
    public double ViewportShare
    {
        get => _viewportShare;
        set
        {
            _viewportShare = value;
            if (IsLoaded) AttachViewport();
            ApplyPlotHeight();
        }
    }

    // The slot above the control points for the values that frame the curve. The
    // owner fills it with fields it builds and wires itself; empty takes no room.
    public Panel Legend => _legend;

    // The slot after the control points, for the owner's values that belong to
    // a view under the plot (the pause strip's two durations). Empty takes no
    // room.
    public Panel LegendAfter => _legendAfter;

    // The slot that ends the legend's row, at its trailing edge (the composer's
    // reset, beside the values it restores). Empty takes no room.
    public Panel LegendTrailing => _trailing;

    // A view under the plot at another scale, between the plot and the legend
    // (the pause strip). Null takes no room. The owner builds and wires it.
    public UIElement? PlotFooter
    {
        get => _footer.Child;
        set
        {
            _footer.Child = value;
            Reflow(ActualWidth);
        }
    }

    // Caption over a field, the form the four control points use, so an owner's
    // legend field looks like them. The field takes the legend's one width; a
    // caption longer than the column ends in an ellipsis rather than widening
    // it, so the columns of every block stay aligned.
    public static FrameworkElement LabelledField(string caption, FrameworkElement field)
    {
        field.Width = FieldWidth;
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(field);
        return stack;
    }

    // The legend's one grid: fields two to a row, in columns of FieldWidth, at
    // the same spacing for the owner's block and the control points, so the
    // second column starts at the same x in both. Fixed columns, not Auto: an
    // Auto column follows its widest child, and the two blocks hold different
    // children.
    public static Grid FieldGrid(IReadOnlyList<FrameworkElement> fields)
    {
        var grid = new Grid { ColumnSpacing = FieldColumnGap, RowSpacing = FieldRowGap };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FieldWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FieldWidth) });
        for (int i = 0; i < fields.Count; i++)
        {
            if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(fields[i], i / 2);
            Grid.SetColumn(fields[i], i % 2);
            grid.Children.Add(fields[i]);
        }
        return grid;
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
        _plotFrame.Width = axes ? double.NaN : SquareSide;
        _plotFrame.HorizontalAlignment = axes ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        _guideStart.Visibility = axes ? Visibility.Visible : Visibility.Collapsed;
        _guideEnd.Visibility = axes ? Visibility.Visible : Visibility.Collapsed;
        ApplyPlotHeight();
        Reflow(ActualWidth);
        Redraw();
    }

    private void ApplyPlotHeight()
    {
        if (!Frame.Axes)
        {
            _plotFrame.Height = SquareSide;
            return;
        }
        double height = WideHeight;
        if (_viewportShare > 0 && _viewport is { ViewportHeight: > 0 } viewport)
            height = Math.Clamp(viewport.ViewportHeight * _viewportShare, MinShareHeight, MaxShareHeight);
        if (_plotFrame.Height != height) _plotFrame.Height = height;
    }

    // The nearest ScrollViewer around the editor, the page's: its visible height
    // is what "most of the window" means for a page that scrolls.
    private void AttachViewport()
    {
        DetachViewport();
        if (_viewportShare <= 0) return;
        DependencyObject? node = VisualTreeHelper.GetParent(this);
        while (node is not null and not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        _viewport = node as ScrollViewer;
        if (_viewport is not null) _viewport.SizeChanged += OnViewportSizeChanged;
        ApplyPlotHeight();
    }

    private void DetachViewport()
    {
        if (_viewport is not null) _viewport.SizeChanged -= OnViewportSizeChanged;
        _viewport = null;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => ApplyPlotHeight();

    // ── Layout ───────────────────────────────────────────────────────────────

    private void ApplyAxisLegend()
    {
        bool hasX = !string.IsNullOrWhiteSpace(XAxisLegend);
        bool hasY = !string.IsNullOrWhiteSpace(YAxisLegend);
        _xLegendName.Text = XAxisLegend ?? string.Empty;
        _yLegendName.Text = YAxisLegend ?? string.Empty;
        _xLegendItem.Visibility = hasX ? Visibility.Visible : Visibility.Collapsed;
        _yLegendItem.Visibility = hasY ? Visibility.Visible : Visibility.Collapsed;
        _axisLegend.Visibility = hasX || hasY ? Visibility.Visible : Visibility.Collapsed;
        // The legend widens the side column, so the stacking threshold follows.
        Reflow(ActualWidth);
    }

    // One legend entry: the axis letter as a quiet caption over its name.
    // The brushes are set by ApplyBrushes, so they follow the theme.
    private FrameworkElement BuildLegendItem(string axis, TextBlock name)
    {
        Style? caption = Application.Current.Resources["CaptionTextBlockStyle"] as Style;
        name.Style = caption;
        name.TextWrapping = TextWrapping.WrapWholeWords;
        var letter = new TextBlock { Text = axis, Style = caption };
        _legendLetters.Add(letter);
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(letter);
        stack.Children.Add(name);
        return stack;
    }

    // Three layouts. With axes (the cut curve), the plot takes the full width,
    // the footer view sits under it, and the legend under both: its blocks side
    // by side when they fit the width, stacked when they do not, the trailing
    // slot at the row's end. Without axes (the square), the legend sits beside
    // the plot, or under it below a comfortable width, as before. Only rows and
    // columns that hold a visible element are declared (a Grid pays its spacing
    // even before an empty one), and nothing is rebuilt when the layout is the
    // one already in place.
    private (bool Axes, bool Stack, bool Footer, bool Legend, bool After, bool Trailing) _layout;
    private bool _laidOut;

    private void Reflow(double width)
    {
        _legend.Visibility = Shown(_legend.Children.Count > 0);
        _legendAfter.Visibility = Shown(_legendAfter.Children.Count > 0);
        _trailing.Visibility = Shown(_trailing.Children.Count > 0);
        _footer.Visibility = Shown(_footer.Child is not null);

        bool axes = Frame.Axes;
        bool footer = _footer.Child is not null;
        bool stack = axes
            ? width > 0 && LegendRowWidth() > width
            : width > 0 && width < StackBelowWidth + (_axisLegend.Visibility == Visibility.Visible ? AxisLegendWidth + 24 : 0);
        var layout = (axes, stack, footer, _legend.Children.Count > 0, _legendAfter.Children.Count > 0, _trailing.Children.Count > 0);
        if (_laidOut && layout == _layout) return;
        _layout = layout;
        _laidOut = true;

        _root.RowDefinitions.Clear();
        _root.ColumnDefinitions.Clear();
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (axes)
        {
            int row = 0;
            Place(_root, _plotFrame, row++, 0);
            if (footer) Place(_root, _footer, row++, 0);
            Place(_root, _side, row++, 0);
            for (int i = 0; i < row; i++) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int row = 0;
            Place(_root, _plotFrame, row++, 0, stack ? 2 : 1);
            if (footer) Place(_root, _footer, row++, 0, stack ? 2 : 1);
            if (stack) Place(_root, _side, row++, 0, 2);
            else { Place(_root, _side, 0, 1); Grid.SetRowSpan(_side, row); }
            for (int i = 0; i < row; i++) _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        // Top in every layout: centred beside a tall plot, the fields floated
        // mid-height with nothing to align to (2026-10-14).
        _side.VerticalAlignment = VerticalAlignment.Top;
        LayoutLegend(horizontal: axes && !stack, stretch: axes);
    }

    // The legend's blocks in one row (each block a column, a star column
    // before the trailing slot so it hugs the edge) or one column.
    private void LayoutLegend(bool horizontal, bool stretch)
    {
        _side.RowDefinitions.Clear();
        _side.ColumnDefinitions.Clear();
        var blocks = new List<FrameworkElement>();
        foreach (FrameworkElement block in new FrameworkElement[] { _legend, _pointsRow, _legendAfter })
            if (block.Visibility == Visibility.Visible) blocks.Add(block);
        bool trailing = _trailing.Visibility == Visibility.Visible;

        if (horizontal)
        {
            _side.ColumnSpacing = BlockColumnGap;
            _side.RowSpacing = 0;
            _side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int column = 0;
            foreach (FrameworkElement block in blocks)
            {
                _side.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Place(_side, block, 0, column++);
            }
            if (trailing)
            {
                _side.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _side.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Place(_side, _trailing, 0, column + 1);
                _trailing.HorizontalAlignment = HorizontalAlignment.Right;
            }
            return;
        }

        _side.ColumnSpacing = 0;
        _side.RowSpacing = stretch ? BlockRowGap : 12;
        _side.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
        });
        int r = 0;
        foreach (FrameworkElement block in blocks)
        {
            _side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_side, block, r++, 0);
        }
        if (trailing)
        {
            _side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(_side, _trailing, r, 0);
            _trailing.HorizontalAlignment = stretch ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }

    // What the legend's blocks need side by side: their own widths, measured
    // unconstrained, plus the gaps; the trailing slot counts with its gap.
    private double LegendRowWidth()
    {
        double total = 0;
        int count = 0;
        foreach (FrameworkElement block in new FrameworkElement[] { _legend, _pointsRow, _legendAfter, _trailing })
        {
            if (block.Visibility != Visibility.Visible) continue;
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            total += block.DesiredSize.Width;
            count++;
        }
        return total + Math.Max(0, count - 1) * BlockColumnGap;
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        Grid.SetRowSpan(element, 1);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // The control points in the legend's one grid, the readout under it rather
    // than spanning its columns, so its monospace width never moves them.
    private void BuildFieldGrid()
    {
        _fields.Children.Add(FieldGrid(
        [
            LabelledField("x1", _x1Box),
            LabelledField("y1", _y1Box),
            LabelledField("x2", _x2Box),
            LabelledField("y2", _y2Box),
        ]));
        _fields.Children.Add(_readout);
    }

    private NumberBox BuildField()
    {
        var box = new NumberBox
        {
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            thumb, Loc.Format("CurveEditor_ControlPoint_Format", first ? 1 : 2));

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
            _dragPixel = Geometry().FromUnit(first ? X1 : X2, first ? Y1 : Y2);
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

        _dragPixel = new Point(_dragPixel.X + e.HorizontalChange, _dragPixel.Y + e.VerticalChange);
        (double nu, double nv) = g.ToUnit(_dragPixel);
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
            (double x, double y) = ToData(p);
            return (Frame.UnitU(x), Frame.UnitV(y));
        }

        public (double X, double Y) ToData(Point p)
            => ((p.X - Left) / (Right - Left) * XMax, (Bottom - p.Y) / (Bottom - Top) * YMax);
    }

    // The frame as drawn: the one an endpoint drag is moving while it is in
    // flight, the owner's otherwise.
    private CurveFrame DrawnFrame => _frameDrag ?? Frame;

    private PlotRect Geometry() => new(_plot.ActualWidth, _plot.ActualHeight, DrawnFrame);

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
        PlaceEndpoints(g, p0, p3);

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
                TextAlignment = TextAlignment.Left,
                Width = GutterLeft - 4,
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

    private static double NiceStep(double range, int targetTicks) => AxisTicks.NiceStep(range, targetTicks);

    private static void SetLine(Line line, Point a, Point b)
    {
        line.X1 = a.X; line.Y1 = a.Y; line.X2 = b.X; line.Y2 = b.Y;
    }

    private void ApplyBrushes()
    {
        _curve.Stroke = Brush("AccentFillColorDefaultBrush");
        _stem1.Stroke = Brush("AccentFillColorDefaultBrush");
        _stem2.Stroke = Brush("AccentFillColorDefaultBrush");
        _reference.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _guideStart.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _guideEnd.Stroke = Brush("DividerStrokeColorDefaultBrush");
        _floor.Stroke = Brush("SystemFillColorCautionBrush");
        _readout.Foreground = Brush("TextFillColorSecondaryBrush");
        _xLegendName.Foreground = Brush("TextFillColorSecondaryBrush");
        _yLegendName.Foreground = Brush("TextFillColorSecondaryBrush");
        foreach (TextBlock letter in _legendLetters) letter.Foreground = Brush("TextFillColorTertiaryBrush");
        Redraw();
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
