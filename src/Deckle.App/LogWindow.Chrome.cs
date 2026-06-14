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
using Deckle.App.Diagnostics;
using Deckle.Core.Interop;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    // ── Theme tracing ────────────────────────────────────────────────────────
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
        // Mutating ImageSource in-place on the existing ImageIconSource does not
        // propagate visually to TitleBar (no routed PropertyChanged). Fix:
        // rebuild a complete ImageIconSource and reassign IconSource.
        AppTitleBar.IconSource = new Microsoft.UI.Xaml.Controls.ImageIconSource
        {
            ImageSource = isRecording ? _iconRecording : _iconIdle,
        };

        // Window icon (titlebar + taskbar + alt-tab): follows the same state.
        // AppWindow.SetIcon expects an .ico file path on disk.
        var path = isRecording ? _iconRecordingPath : _iconIdlePath;
        if (path is not null) AppWindow.SetIcon(path);
    }

    private void LoadAppIcons()
    {
        _iconIdlePath      = IconAssets.ResolvePath(recording: false);
        _iconRecordingPath = IconAssets.ResolvePath(recording: true);

        if (_iconIdlePath is not null)
            _iconIdle = new BitmapImage(new Uri(_iconIdlePath));
        else
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail("idle icon not found");
        }

        if (_iconRecordingPath is not null)
            _iconRecording = new BitmapImage(new Uri(_iconRecordingPath));
        else
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail("recording icon not found");
        }
    }

    // ── TitleBar search: responsive collapse ──────────────────────────────────

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
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
        // Only retract when window is narrow; in wide mode the SearchBox is
        // permanently visible. Also retract only if the user didn't leave a
        // non-empty filter behind, so the search remains reachable to clear it.
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
