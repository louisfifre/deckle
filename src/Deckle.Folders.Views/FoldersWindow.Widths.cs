using Deckle.Shell.WindowChrome;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// What the window's width changes in the top row and the details panel. The
// navigation pane follows the width by itself (NavigationPane docks it from
// 641 and lays it over the page below); this file applies the rest from one
// handler on the root grid, with the breakpoints Windows draws its own pages
// at, in DIPs of the window's content width.
public sealed partial class FoldersWindow
{
    // NavigationView's ExpandedModeThresholdWidth: from here the tools show
    // their names beside their glyphs, below only the glyphs.
    private const double ToolNamesFromWidth = 1008;

    // NavigationView's CompactModeThresholdWidth, the width NavigationPane
    // docks from: below it the window is the narrow layout.
    private const double NarrowBelowWidth = NavigationPane.DefaultDockedFromWidth;

    // The title's text is not hidden here: the TitleBar control collapses it
    // by itself when its Content (the whole row below) needs the room, and
    // gives it back from a content width of 947 DIPs (a 960 px window at 100%
    // scale), where the address field in its full form still holds 253 DIPs
    // with the tools as glyphs. Measured on the running window; that is the
    // width this layout wants the title from, and the control's own rule lands
    // on it, so the narrow layout's hidden title (the app icon stays) needs no
    // code of ours either.

    // The tools' names, hidden while the glyphs stand alone.
    private readonly List<TextBlock> _toolNames = [];

    // The title bar keeps a strip free of Content before the caption buttons,
    // TitleBarMinDragRegionWidth (48) in WinAppSDK's generic.xaml, so the
    // window can always be dragged. In the narrow layout that strip is room
    // the address field lacks, but the template reads the value once, when it
    // applies, and reaching into its numbered columns to change it would break
    // at the next WinUI update: Windows' value stays.

    // The app icon as ApplyChrome set it, put back when the window widens.
    private IconSource? _titleIcon;

    // Whether the window is below NarrowBelowWidth. It changes what the details
    // toggle shows (FoldersWindow.Tools.cs), so UpdateDetailsPanel reads it.
    private bool _narrow;

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => ApplyWidth(e.NewSize.Width);

    // Everything is derived from the width alone, so going back to a width
    // gives back what it showed there.
    private void ApplyWidth(double width)
    {
        bool narrow = width < NarrowBelowWidth;
        _titleIcon ??= AppTitleBar.IconSource;
        AppTitleBar.IconSource = narrow ? null : _titleIcon;
        Visibility names = width >= ToolNamesFromWidth ? Visibility.Visible : Visibility.Collapsed;
        foreach (TextBlock name in _toolNames) name.Visibility = names;

        // F5 and Alt+Right still work: the accelerators belong to the root.
        Visibility wide = narrow ? Visibility.Collapsed : Visibility.Visible;
        ForwardButton.Visibility = wide;
        RefreshButton.Visibility = wide;
        Address.ShowsCurrentOnly = narrow;

        if (narrow == _narrow) return;
        _narrow = narrow;
        UpdateDetailsPanel();
    }
}
