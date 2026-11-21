using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// The map's filter boxes, laid left to right and carried to a new line when
// the next one would not fit, so no label is cut when the side panel narrows
// the list (spec silent: Explorer's command bar folds, it never clips a word).
// WinUI ships no wrapping panel, and the toolkit's is not referenced here; this
// one does only what the filter line needs: a gap between items, a gap between
// lines, each item centred in the height of its line.
internal sealed partial class MapFilterPanel : Panel
{
    public double ColumnSpacing { get; set; }

    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var free = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement child in Children)
            child.Measure(free);
        return Arrange(availableSize.Width, place: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Arrange(finalSize.Width, place: true);
        return finalSize;
    }

    // Walks the children line by line; with place set, puts each one where its
    // line and its rank say. Returns the size the lines take.
    private Size Arrange(double width, bool place)
    {
        double used = 0, top = 0;
        int start = 0;
        while (start < Children.Count)
        {
            // Gather the children that fit on this line, at least one.
            double lineWidth = 0, lineHeight = 0;
            int end = start;
            for (; end < Children.Count; end++)
            {
                Size wanted = Children[end].DesiredSize;
                if (wanted.Width == 0 && wanted.Height == 0)
                    continue;
                double next = lineWidth == 0 ? wanted.Width : lineWidth + ColumnSpacing + wanted.Width;
                if (lineWidth > 0 && next > width)
                    break;
                lineWidth = next;
                lineHeight = Math.Max(lineHeight, wanted.Height);
            }

            if (place)
            {
                double left = 0;
                for (int i = start; i < end; i++)
                {
                    Size wanted = Children[i].DesiredSize;
                    if (wanted.Width == 0 && wanted.Height == 0)
                    {
                        Children[i].Arrange(new Rect(left, top, 0, 0));
                        continue;
                    }
                    Children[i].Arrange(new Rect(left, top + (lineHeight - wanted.Height) / 2, wanted.Width, wanted.Height));
                    left += wanted.Width + ColumnSpacing;
                }
            }

            used = Math.Max(used, lineWidth);
            top += lineHeight + RowSpacing;
            start = end;
        }

        return new Size(used, Math.Max(0, top - RowSpacing));
    }
}
