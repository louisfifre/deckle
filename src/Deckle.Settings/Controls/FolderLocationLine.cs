using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── FolderLocationLine ──────────────────────────────────────────────────────
//
// The Location line of a folder setting, the one folder component of the
// settings surfaces (the General page's "Settings backup" form, Louis,
// 2026-10-15): a SettingsCard headed "Location", the folder's path as its
// description, and at the trailing edge "Choose a folder" (which opens the OS
// folder picker) then "Open" (which opens the folder in Explorer), both as
// words. The Editable mode puts a field in the path's place, for a path typed
// or pasted from another machine rather than browsed to; the OpenOnly mode,
// for a folder the app owns, offers Open alone.
//
// Line is a plain SettingsCard, built here rather than subclassed, so a fold
// can host it in Items (which accepts SettingsCard instances only) and the
// expander's item style applies to it as to any card. The composer
// (SettingsComposer.BuildPathLine) drives it through IPathControl and puts its
// reset wheel ahead of Actions; a hand-authored page can place Line directly.
// The control lives here, not in Catalog, because the picker anchors to the
// Settings window and the failures log through this module's ETW source.
//
// An empty Path means the default folder: the line shows DefaultPath, and Open
// goes there, where the data actually lands. PathChanged is raised on the
// person's gesture only (a pick, a field commit), never on a Path the composer
// pushes, so a model refresh cannot write back.
public sealed class FolderLocationLine : IPathControl
{
    private const string Library = "Deckle.Settings";

    private readonly FolderPickerMode _mode;
    private readonly string _defaultPath;
    private readonly TextBlock? _pathText;
    private readonly TextBox? _pathBox;
    private string _path = string.Empty;

    public SettingsCard Line { get; }
    public FrameworkElement Actions { get; }

    public event EventHandler? PathChanged;

    public FolderLocationLine(FolderPickerMode mode, string defaultPath)
    {
        _mode = mode;
        _defaultPath = defaultPath ?? string.Empty;

        Line = new SettingsCard { Header = Loc.GetFrom(Library, "FolderLocationLine_Location") };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Actions = actions;

        if (mode == FolderPickerMode.Editable)
        {
            // The field shows the path, so the line keeps no description. Wider
            // than the composer's text field (200): a path is long. Written at
            // the field's commit, Enter or focus loss, the gesture rule.
            _pathBox = new TextBox
            {
                MinWidth = 240,
                VerticalAlignment = VerticalAlignment.Center,
                PlaceholderText = _defaultPath,
            };
            _pathBox.LostFocus += (_, _) => CommitField();
            _pathBox.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                CommitField();
                e.Handled = true;
            };
            actions.Children.Add(_pathBox);
        }
        else
        {
            // The path in the description's type and colour, inherited from the
            // card's description presenter. It never wraps: a long path is
            // trimmed with an ellipsis and the tooltip carries it whole; the
            // text can be selected to copy it.
            _pathText = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsTextSelectionEnabled = true,
            };
            Line.Description = _pathText;
        }

        if (mode != FolderPickerMode.OpenOnly)
        {
            var choose = new Button { Content = Loc.GetFrom(Library, "FolderLocationLine_ChooseFolder") };
            choose.Click += ChooseButton_Click;
            actions.Children.Add(choose);
        }

        var open = new Button { Content = Loc.GetFrom(Library, "FolderLocationLine_Open") };
        open.Click += OpenButton_Click;
        actions.Children.Add(open);

        Line.Content = actions;
        ShowPath();
    }

    public string Path
    {
        get => _path;
        set
        {
            _path = value ?? string.Empty;
            ShowPath();
        }
    }

    private void ShowPath()
    {
        if (_pathBox is not null)
        {
            if (_pathBox.Text != _path) _pathBox.Text = _path;
            return;
        }

        string shown = string.IsNullOrEmpty(_path) ? _defaultPath : _path;
        _pathText!.Text = shown;
        ToolTipService.SetToolTip(_pathText, string.IsNullOrEmpty(shown) ? null : shown);
    }

    private void CommitField()
    {
        string typed = _pathBox!.Text;
        if (typed == _path) return;
        _path = typed;
        PathChanged?.Invoke(this, EventArgs.Empty);
    }

    // Use the Windows App SDK picker (Microsoft.Windows.Storage.Pickers, 1.7+),
    // never the UWP Windows.Storage.Pickers one: the UWP picker does not work
    // under elevation and needs the InitializeWithWindow interop.
    private async void ChooseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = SettingsHost.GetSettingsWindow?.Invoke()
                ?? throw new InvalidOperationException("Settings window not initialized");

            // The picker's Win32 dialog HWND is not exposed by the API, so its
            // position and size are unknowable: the probe records the trigger
            // button's rect as the anchoring intent, dialog rect at zero.
            EmitFolderPickerAnchor(sender as FrameworkElement, window);

            // This PC, not Documents: the folders chosen here (data root,
            // models, telemetry, backups) are storage locations, and the data
            // root in particular usually goes to another drive, where the old
            // Move button already opened.
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            };

            var result = await picker.PickSingleFolderAsync();
            if (result is null) return;

            Path = result.Path;
            PathChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // TransformToVisual(null) yields the button position in DIPs, while
    // AppWindow.Position is in screen pixels: scale by the window DPI first.
    internal static void EmitFolderPickerAnchor(FrameworkElement? trigger, Microsoft.UI.Xaml.Window window)
    {
        if (trigger is null) { WindowingProbe.EmitPopupAnchored(IntPtr.Zero, "folder-picker", 0, 0, 0, 0); return; }

        IntPtr hwnd = WindowNative.GetWindowHandle(window);
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(hwnd));
        var transform = trigger.TransformToVisual(null);
        var pt = transform.TransformPoint(default);

        int parent_x = window.AppWindow.Position.X + (int)Math.Round(pt.X * scale);
        int parent_y = window.AppWindow.Position.Y + (int)Math.Round(pt.Y * scale);
        int parent_w = (int)Math.Round(trigger.ActualWidth  * scale);
        int parent_h = (int)Math.Round(trigger.ActualHeight * scale);

        WindowingProbe.EmitPopupAnchored(
            IntPtr.Zero, "folder-picker",
            parent_x, parent_y, parent_w, parent_h);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        string target = string.IsNullOrEmpty(Path) ? _defaultPath : Path;
        if (string.IsNullOrEmpty(target)) return;

        try
        {
            Directory.CreateDirectory(target);
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
