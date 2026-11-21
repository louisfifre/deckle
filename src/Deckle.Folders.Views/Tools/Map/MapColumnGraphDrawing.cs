using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Deckle.FolderMap;

namespace Deckle.Folders.Views;

// The large graph's drawing as it stands until Louis decides it (2026-11-15):
// deliberately plain, like the panel's small graph. One column per kind under
// its title, each item a button that names it, a line for each link with an
// arrow at the end that is cited, "+N" at the foot of a column that leaves
// items out. Where everything sits comes from MapGraphLayout, so the same
// inventory always draws the same; this class only turns rows and columns
// into pixels. No physics, no pan, no zoom. Every colour is a theme resource,
// so the graph follows light, dark and contrast.
//
// The chosen item and its neighbours stand out; the rest takes the tertiary
// text and a disabled stroke, as Windows dims what is not current (12.9).
internal sealed partial class MapColumnGraphDrawing : Grid, IMapGraphDrawing
{
    private const double TitleHeight = 32;
    private const double RowStep = 44;
    private const double NodeGap = 6;
    private const double ArrowLength = 7;
    private const double ArrowHalfWidth = 3.5;

    private readonly Canvas _canvas = new();
    private MapInventory? _map;
    private MapListFilter _filter = MapListFilter.All;
    private MapItem? _chosen;
    private Size _drawnFor;
    private (int Drawn, int Hidden)? _trimLogged;

    public MapColumnGraphDrawing()
    {
        Children.Add(_canvas);
        AutomationProperties.SetName(this, FoldersText.Get("Map_LargeGraph_Name"));
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _drawnFor.Width) > 0.5 || Math.Abs(e.NewSize.Height - _drawnFor.Height) > 0.5)
                Draw(keepFocus: false);
        };
    }

    public FrameworkElement Element => this;

    public event EventHandler<MapItem>? ItemChosen;

    public void Show(MapInventory map, MapListFilter filter, MapItem? chosen)
    {
        _map = map;
        _filter = filter;
        _chosen = chosen;
        Draw(keepFocus: XamlRoot is { } root && FocusManager.GetFocusedElement(root) is DependencyObject focused && IsInside(focused));
    }

    private void Draw(bool keepFocus)
    {
        double width = ActualWidth, height = ActualHeight;
        _drawnFor = new Size(width, height);
        _canvas.Children.Clear();
        if (_map is not { } map || width <= 0 || height <= 0) return;

        int maxRows = Math.Max(1, (int)((height - TitleHeight) / RowStep));
        MapGraphLayout layout = MapGraphLayout.Of(map, _filter, _chosen, maxRows);
        LogTrim(layout);
        if (layout.Columns.Count == 0) return;

        double columnWidth = width / layout.Columns.Count;
        var boxes = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<UIElement>();
        Button? focusTarget = null;

        for (int c = 0; c < layout.Columns.Count; c++)
        {
            MapGraphColumn column = layout.Columns[c];
            double centreX = columnWidth * (c + 0.5);
            var title = new TextBlock
            {
                Text = MapKindText.Plural(column.Kind),
                Style = Resource<Style>("CaptionTextBlockStyle"),
                Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Max(0, columnWidth - 16),
            };
            Place(title, new Point(centreX, TitleHeight / 2), width, height);
            nodes.Add(title);

            if (column.Hidden > 0)
            {
                var more = new TextBlock
                {
                    Text = FoldersText.Format("Map_Graph_More_Format", column.Hidden),
                    Style = Resource<Style>("CaptionTextBlockStyle"),
                    Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
                };
                AutomationProperties.SetName(more, FoldersText.Format("Map_LargeGraph_More_Name_Format", column.Hidden));
                Place(more, new Point(centreX, RowCentre(column.Drawn)), width, height);
                nodes.Add(more);
            }
        }

        foreach (MapGraphNode node in layout.Nodes)
        {
            double columnWidthMax = Math.Max(48, columnWidth - 16);
            Button button = Node(node, columnWidthMax);

            // A button's fill is translucent: a line passing behind an item
            // would cross its name. The opaque backing under it, in the
            // colour of the layer, hides the line there.
            var backing = new Border
            {
                Background = Resource<Brush>("SolidBackgroundFillColorTertiaryBrush"),
                CornerRadius = Resource<CornerRadius>("ControlCornerRadius"),
                Child = button,
            };
            Rect box = Place(backing, new Point(columnWidth * (node.Column + 0.5), RowCentre(node.Row)), width, height);
            boxes[node.Item.Id] = box;
            nodes.Add(backing);
            if (node.IsChosen) focusTarget = button;
        }

        // The dimmed lines first, so the ones that stand out cross over them.
        var edges = new List<UIElement>();
        foreach (MapGraphEdge edge in layout.Edges.OrderBy(e => e.StandsOut))
            edges.AddRange(Edge(boxes[edge.From.Id], boxes[edge.To.Id], edge));

        // Placing measured the nodes on the canvas; they go back on it after
        // the lines, so the nodes cover the lines' ends.
        _canvas.Children.Clear();
        foreach (UIElement edge in edges) _canvas.Children.Add(edge);
        foreach (UIElement node in nodes) _canvas.Children.Add(node);

        if (keepFocus) focusTarget?.Focus(FocusState.Programmatic);
    }

    private static double RowCentre(int row) => TitleHeight + RowStep * (row + 0.5);

    private Button Node(MapGraphNode node, double maxWidth)
    {
        MapItem item = node.Item;
        Brush text = Resource<Brush>(node.StandsOut ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon
        {
            Glyph = MapKindText.Glyph(item.Kind),
            FontSize = 12,
            Foreground = Resource<Brush>(node.StandsOut ? "TextFillColorSecondaryBrush" : "TextFillColorTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = item.Name,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            FontWeight = node.IsChosen ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = Math.Max(0, maxWidth - 40),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 0,
            Content = content,
            Tag = item,
        };
        if (node.IsChosen)
        {
            button.Background = Resource<Brush>("SubtleFillColorSecondaryBrush");
            button.BorderBrush = Resource<Brush>("ControlStrongStrokeColorDefaultBrush");
        }
        else if (!node.StandsOut)
        {
            button.Background = Resource<Brush>("ControlFillColorTertiaryBrush");
            button.BorderBrush = Resource<Brush>("ControlStrokeColorSecondaryBrush");
        }
        button.Click += (_, _) => ItemChosen?.Invoke(this, item);
        ToolTipService.SetToolTip(button, MapKindText.Place(item));
        AutomationProperties.SetName(button, FoldersText.Format("Map_LargeGraph_Node_Name_Format", item.Name, MapKindText.Name(item.Kind)));
        return button;
    }

    // Puts an element centred on a point, kept inside the drawing.
    private Rect Place(FrameworkElement element, Point at, double width, double height)
    {
        _canvas.Children.Add(element);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = element.DesiredSize;
        double left = Math.Clamp(at.X - size.Width / 2, 0, Math.Max(0, width - size.Width));
        double top = Math.Clamp(at.Y - size.Height / 2, 0, Math.Max(0, height - size.Height));
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return new Rect(left, top, size.Width, size.Height);
    }

    // A link between two boxes, from edge to edge, with its arrow heads at
    // the end that is cited. Two items of one column would be joined by a
    // straight line through the items between them: the line bends out to
    // the right of the column instead.
    private IEnumerable<UIElement> Edge(Rect from, Rect to, MapGraphEdge edge)
    {
        Brush stroke = Resource<Brush>(edge.StandsOut ? "TextFillColorSecondaryBrush" : "ControlStrongStrokeColorDisabledBrush");
        bool sameColumn = Math.Abs(Mid(from).X - Mid(to).X) < 1;
        Point start, end, startToward, endToward;
        UIElement line;
        if (sameColumn)
        {
            double bulge = Math.Max(from.Right, to.Right) + 16 + Math.Abs(Mid(to).Y - Mid(from).Y) * 0.15;
            var control = new Point(bulge, (Mid(from).Y + Mid(to).Y) / 2);
            start = Border(from, control);
            end = Border(to, control);
            startToward = endToward = control;
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new QuadraticBezierSegment { Point1 = control, Point2 = end });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            line = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = stroke, StrokeThickness = 1 };
        }
        else
        {
            start = Border(from, Mid(to));
            end = Border(to, Mid(from));
            startToward = end;
            endToward = start;
            line = new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = stroke, StrokeThickness = 1 };
        }

        yield return line;
        yield return Arrow(endToward, end, stroke);
        if (edge.BothWays) yield return Arrow(startToward, start, stroke);
    }

    private static Polygon Arrow(Point from, Point tip, Brush fill)
    {
        double dx = tip.X - from.X, dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return new Polygon();
        double ux = dx / length, uy = dy / length;
        var baseCentre = new Point(tip.X - ux * ArrowLength, tip.Y - uy * ArrowLength);
        return new Polygon
        {
            Points =
            {
                tip,
                new Point(baseCentre.X - uy * ArrowHalfWidth, baseCentre.Y + ux * ArrowHalfWidth),
                new Point(baseCentre.X + uy * ArrowHalfWidth, baseCentre.Y - ux * ArrowHalfWidth),
            },
            Fill = fill,
        };
    }

    private static Point Mid(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    // Where the line from the box's middle towards a point leaves the box,
    // a few pixels out so the arrow head does not touch the border.
    private static Point Border(Rect box, Point towards)
    {
        Point c = Mid(box);
        double dx = towards.X - c.X, dy = towards.Y - c.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return c;
        double hw = box.Width / 2 + NodeGap, hh = box.Height / 2 + NodeGap;
        double scale = Math.Min(
            Math.Abs(dx) < 0.001 ? double.PositiveInfinity : hw / Math.Abs(dx),
            Math.Abs(dy) < 0.001 ? double.PositiveInfinity : hh / Math.Abs(dy));
        return new Point(c.X + dx * scale, c.Y + dy * scale);
    }

    // Once per change of what is left out, not at every resize that keeps it.
    private void LogTrim(MapGraphLayout layout)
    {
        (int, int)? trim = layout.Hidden > 0 ? (layout.Nodes.Count, layout.Hidden) : null;
        if (trim == _trimLogged) return;
        _trimLogged = trim;
        if (trim is not null) DeckleFoldersSource.Log.MapGraphTrimmed(layout.Nodes.Count, layout.Hidden, layout.IsNeighbourhood);
    }

    private bool IsInside(DependencyObject element)
    {
        for (DependencyObject? e = element; e is not null; e = VisualTreeHelper.GetParent(e))
            if (ReferenceEquals(e, this)) return true;
        return false;
    }

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
