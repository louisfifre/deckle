// LogWindow — responsive titlebar search, app icon/beacon, and theme tracing.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Data;
using WinRT.Interop;
using Deckle.App;
using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private Microsoft.UI.Xaml.ElementTheme _lastTheme;

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        var to = sender.ActualTheme;
        if (to == _lastTheme) return;
        string source = ThemeRequestSourceProbe.Consume() ?? "system";
        DeckleThemeSource.Log.ThemeChanged(
            "log", _lastTheme.ToString(), to.ToString(), source);
        _lastTheme = to;
    }

    private void ApplyRecordingState(bool isRecording)
    {
        _isRecording = isRecording;
        // Mutating ImageSource on the existing ImageIconSource never reaches the
        // TitleBar; only a new ImageIconSource does.
        AppTitleBar.IconSource = new Microsoft.UI.Xaml.Controls.ImageIconSource
        {
            ImageSource = isRecording ? _iconRecording : _iconIdle,
        };

        IconAssets.ApplyToWindow(AppWindow, isRecording);
    }

    // The title bar shows the bare mark; the window's own icon, a tile, is
    // IconAssets' to pick.
    private void LoadAppIcons()
    {
        string? idlePath      = IconAssets.ResolveImagePath(recording: false);
        string? recordingPath = IconAssets.ResolveImagePath(recording: true);

        if (idlePath is not null)
            _iconIdle = new BitmapImage(new Uri(idlePath));
        else
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "log", "icon_load", "idle_icon_not_found", "", "");
        }

        if (recordingPath is not null)
            _iconRecording = new BitmapImage(new Uri(recordingPath));
        else
        {
            DeckleAppSource.Log.WindowOperationFailed(
                "log", "icon_load", "recording_icon_not_found", "", "");
        }
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        // The toolbar's two groups share one line; below this width the
        // settings label gives way to its icon so they never overlap.
        SettingsButtonText.Visibility = args.Size.Width < ToolbarCompactWidth
            ? Visibility.Collapsed
            : Visibility.Visible;

        bool narrow = args.Size.Width < SearchCollapseThreshold;
        if (narrow == _isSearchNarrow) return;
        _isSearchNarrow = narrow;
        if (narrow) ShowSearchIcon();
        else ShowSearchBox();
    }

    private void OnSearchIconClick(object sender, RoutedEventArgs e)
    {
        ShowSearchBox();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // A non-empty search stays visible so the user can still clear it.
        if (!_isSearchNarrow) return;
        if (!string.IsNullOrEmpty(SearchBox.Text)) return;
        ShowSearchIcon();
    }

    private void ShowSearchBox()
    {
        SearchIconButton.Visibility = Visibility.Collapsed;
        SearchBox.Visibility = Visibility.Visible;
    }

    private void ShowSearchIcon()
    {
        SearchBox.Visibility = Visibility.Collapsed;
        SearchIconButton.Visibility = Visibility.Visible;
    }
}
