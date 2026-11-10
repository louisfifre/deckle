using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Deckle.Atelier.Views;

// The panel's small graph (spec, Map: "a square as wide as the panel"; board
// 12.8a): the item in the middle, its neighbours around it, an arrow for each
// link pointing at the item cited, and "+N" for the links beyond four. Drawn
// with plain shapes on a Canvas: four nodes need no layout engine, and every
// colour is a theme resource, so the graph follows light, dark and contrast.
//
// It shows; it is not worked in (spec: "Louis does not work in it"). The
// lists under it say the same in words, for a screen reader too.
internal sealed partial class MapLinkGraph : Grid
{
    private const double NodeGap = 6;
    private const double ArrowLength = 7;
    private const double ArrowHalfWidth = 3.5;

    private readonly MapNeighbourhood _around;
    private readonly Canvas _canvas = new();
    private double _drawnFor = -1;

    public MapLinkGraph(MapNeighbourhood around)
    {
        _around = around;
        Height = 288;
        Children.Add(_canvas);
        AutomationProperties.SetName(this, AtelierText.Format("Map_Graph_Name_Format", around.Centre.Name));
        SizeChanged += OnSizeChanged;
    }

    // As wide as the panel, as tall as wide.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        double side = e.NewSize.Width;
        if (side <= 0) return;
        if (Math.Abs(Height - side) > 0.5) Height = side;
        if (Math.Abs(_drawnFor - side) > 0.5) Draw(side);
    }

    private void Draw(double side)
    {
        _drawnFor = side;
        _canvas.Children.Clear();
        var centre = new Point(side / 2, side / 2);
        double radius = side * 0.34;
        double nodeMaxWidth = side * 0.42;

        Border middle = Node(_around.Centre, nodeMaxWidth, isCentre: true);
        Rect middleBox = Place(middle, centre, side);

        double[] angles = Angles(_around.Shown.Count, _around.Hidden > 0);
        var edges = new List<UIElement>();
        var nodes = new List<UIElement> { middle };
        for (int i = 0; i < _around.Shown.Count; i++)
        {
            MapNeighbour neighbour = _around.Shown[i];
            double a = angles[i] * Math.PI / 180;
            Border node = Node(neighbour.Item, nodeMaxWidth, isCentre: false);
            Rect box = Place(node, new Point(centre.X + radius * Math.Cos(a), centre.Y + radius * Math.Sin(a)), side);
            nodes.Add(node);
            edges.AddRange(Edge(middleBox, box, neighbour.Direction));
        }

        if (_around.Hidden > 0)
        {
            var more = new TextBlock
            {
                Text = AtelierText.Format("Map_Graph_More_Format", _around.Hidden),
                Style = Resource<Style>("CaptionTextBlockStyle"),
                Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            };
            AutomationProperties.SetName(more, AtelierText.Format("Map_Graph_More_Name_Format", _around.Hidden));
            Rect box = Place(more, new Point(centre.X, centre.Y + radius), side);
            Point from = Border(middleBox, Mid(box));
            Point to = Border(box, Mid(middleBox));
            edges.Add(new Line
            {
                X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
                Stroke = Resource<Brush>("ControlStrongStrokeColorDisabledBrush"),
                StrokeThickness = 1,
                StrokeDashArray = [2, 2],
            });
            nodes.Add(more);
        }

        // Placing measured the nodes on the canvas; they go back on it after
        // the lines, so the nodes cover the lines' ends.
        _canvas.Children.Clear();
        foreach (UIElement edge in edges) _canvas.Children.Add(edge);
        foreach (UIElement node in nodes) _canvas.Children.Add(node);
    }

    // Where the neighbours sit, in degrees, 0 to the right and clockwise. Four
    // take the corners, as 12.8a draws them, leaving the bottom to "+N"; fewer
    // spread evenly from the top.
    private static double[] Angles(int count, bool hasMore) => count switch
    {
        0 => [],
        1 => [-90],
        2 => [180, 0],
        3 when !hasMore => [-90, 30, 150],
        _ => [-135, -45, 45, 135],
    };

    private Border Node(MapItem item, double maxWidth, bool isCentre)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon
        {
            Glyph = MapKindText.Glyph(item.Kind),
            FontSize = 12,
            Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = item.Name,
            Style = Resource<Style>("CaptionTextBlockStyle"),
            FontWeight = isCentre ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = maxWidth - 40,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var node = new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = Resource<CornerRadius>("ControlCornerRadius"),
            BorderThickness = new Thickness(1),
            Background = Resource<Brush>(isCentre ? "SubtleFillColorSecondaryBrush" : "CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Resource<Brush>(isCentre ? "ControlStrongStrokeColorDefaultBrush" : "CardStrokeColorDefaultBrush"),
            Child = content,
        };
        ToolTipService.SetToolTip(node, MapKindText.Place(item));
        return node;
    }

    // Puts an element centred on a point, kept inside the square.
    private Rect Place(FrameworkElement element, Point at, double side)
    {
        _canvas.Children.Add(element);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = element.DesiredSize;
        double left = Math.Clamp(at.X - size.Width / 2, 0, Math.Max(0, side - size.Width));
        double top = Math.Clamp(at.Y - size.Height / 2, 0, Math.Max(0, side - size.Height));
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return new Rect(left, top, size.Width, size.Height);
    }

    // A line between two boxes, from edge to edge, with its arrow heads at the
    // end that is cited: at the neighbour when the item cites it, at the item
    // when it is cited, at both ends both ways.
    private IEnumerable<UIElement> Edge(Rect middle, Rect neighbour, MapLinkDirection direction)
    {
        Point from = Border(middle, Mid(neighbour));
        Point to = Border(neighbour, Mid(middle));
        Brush stroke = Resource<Brush>("TextFillColorTertiaryBrush");
        yield return new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, Stroke = stroke, StrokeThickness = 1 };
        if (direction is MapLinkDirection.Out or MapLinkDirection.Both) yield return Arrow(from, to, stroke);
        if (direction is MapLinkDirection.In or MapLinkDirection.Both) yield return Arrow(to, from, stroke);
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

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
