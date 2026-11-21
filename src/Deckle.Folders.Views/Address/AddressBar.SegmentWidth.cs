using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// How wide a segment's name may be. BreadcrumbBar folds the leftmost segments
// into its ellipsis when the path is too long, but always shows the last one
// whole: a single name longer than the bar would run under the tools. Each
// name gets a ceiling that leaves room, inside the bar, for the folder's icon,
// the fold button and the segment's chevron; past it the name is cut with an
// ellipsis (spec, Open 33: "a name too long for its place"), and a cut name
// says itself whole in a tooltip.
internal sealed partial class AddressBar
{
    // Taken from the bar's width before a name: the frame's padding and the
    // icon button, then the chevron and the segment's own padding beside the
    // last name; with more than one segment, the fold button too, where the
    // leftmost segments go when the path is too long.
    private const double SegmentRoomReserved = 6 + 28 + 2 + 28 + 16;
    private const double FoldButtonWidth = 32;
    private const double SegmentNameMinWidth = 24;

    private readonly HashSet<TextBlock> _crumbNames = [];
    private double _crumbNameMaxWidth = double.PositiveInfinity;

    private void OnSegmentsModeSizeChanged(object sender, SizeChangedEventArgs e) => FitSegmentNames();

    // Called when the bar's width changes and when it shows another place.
    private void FitSegmentNames()
    {
        double reserved = SegmentRoomReserved + (_place?.Segments.Count > 1 ? FoldButtonWidth : 0);
        _crumbNameMaxWidth = Math.Max(SegmentNameMinWidth, SegmentsMode.ActualWidth - reserved);
        foreach (TextBlock name in _crumbNames) name.MaxWidth = _crumbNameMaxWidth;
    }

    private void OnCrumbNameLoaded(object sender, RoutedEventArgs e)
    {
        var name = (TextBlock)sender;
        _crumbNames.Add(name);
        name.MaxWidth = _crumbNameMaxWidth;
    }

    private void OnCrumbNameUnloaded(object sender, RoutedEventArgs e) => _crumbNames.Remove((TextBlock)sender);

    private void OnCrumbNameTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args) =>
        ToolTipService.SetToolTip(sender, sender.IsTextTrimmed ? sender.Text : null);

    // What the self-check reads: whether a segment's name is cut now.
    public bool HasTrimmedSegment => _crumbNames.Any(n => n.IsTextTrimmed);
}
