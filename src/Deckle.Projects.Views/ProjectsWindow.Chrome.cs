using Deckle.Core;
using Deckle.Shell;
using Deckle.Shell.WindowChrome;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Deckle.Projects.Views;

// The window's frame: the title bar, the material, the icon and the size.
public sealed partial class ProjectsWindow
{
    // The size the boards are drawn at, in DIPs. Window sizes are physical
    // pixels, so scaled, and clamped to the work area.
    private const int InitialWidth = 1280;
    private const int InitialHeight = 900;

    // The narrowest and shortest the page holds: a single column of cards, the
    // title row with both buttons, and the title bar's controls.
    private const int MinimumWidth = 720;
    private const int MinimumHeight = 480;

    private void ApplyChrome()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // The standard height: the bar carries only the icon and the title, so
        // the caption buttons stay inside it and the page starts right under
        // it. (The Atelier's bar is tall because it holds an address bar.)
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        CaptionInsetCorrection.Attach(AppTitleBar, AppWindow);
        // A window the person works in for a long time wears Mica.
        SystemBackdrop = new MicaBackdrop();

        if (IconAssets.ResolvePath(recording: false) is { } ico) AppWindow.SetIcon(ico);
        if (IconAssets.ResolveImagePath(recording: false) is { } png)
            AppTitleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(png)) };

        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min((int)(InitialWidth * scale), work.Width),
            Math.Min((int)(InitialHeight * scale), work.Height)));

        var presenter = OverlappedPresenter.Create();
        presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
        presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        AppWindow.SetPresenter(presenter);
    }

    // What the title bar shows follows the store and the history: the hamburger
    // only once there is something to list (spec, H0: no pane button before any
    // project is added), and the back button on a category's page and a
    // project's page, never on Home.
    private void UpdateChrome()
    {
        bool empty = _workspace.Store.HoldsNothing();
        AppTitleBar.IsPaneToggleButtonVisible = !empty;
        AppTitleBar.IsBackButtonVisible = _history.CanGoBack && _history.Current is not ProjectsTarget.Home;
        // The pane has nothing to show on the empty screen: no rail either.
        Nav.IsPaneVisible = !empty;
    }
}
