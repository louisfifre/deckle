using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Deckle.Projects.Views;

// Children in a row that wraps to the next line when the width runs out, each at
// its own natural width. Windows App SDK ships no such panel (a WrapPanel lives
// in the Community Toolkit, which this project does not reference, and
// ItemsRepeater's UniformGridLayout gives every item the width of the widest one,
// which spreads "File Explorer" and "Terminal" across cells sized for "Deckle
// Folders"). So this is the one panel the page draws by hand, the size of what it
// does: lay each child after the last, and start a line when one does not fit.
internal sealed class WrapRow : Panel
{
    public double ColumnSpacing { get; set; }

    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(Size available)
    {
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            Size size = child.DesiredSize;

            // A child that does not fit after the others starts a new line,
            // unless it is the first of its line (one that is wider than the
            // row cannot do better).
            if (lineWidth > 0 && lineWidth + ColumnSpacing + size.Width > available.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + RowSpacing;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += (lineWidth > 0 ? ColumnSpacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (UIElement child in Children)
        {
            Size size = child.DesiredSize;
            if (x > 0 && x + ColumnSpacing + size.Width > finalSize.Width)
            {
                y += lineHeight + RowSpacing;
                x = 0;
                lineHeight = 0;
            }

            if (x > 0) x += ColumnSpacing;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        return finalSize;
    }
}
