using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── FolderPickerCard ────────────────────────────────────────────────────────
//
// Read-only display of a folder path with two actions: Set (opens the
// FolderPicker) and Open (opens the path in Explorer). Designed to live
// inside a SettingsCard at the consumer site — that lets the SettingsCard
// own Header / Description / HeaderIcon via x:Uid, and keeps the visual
// tree compatible with SettingsExpander.Items (which rejects UserControl
// wrappers around SettingsCard children).
//
// When Path is empty, the TextBlock shows DefaultPath at reduced opacity:
// a placeholder without the misleading affordance of an editable field.
public sealed partial class FolderPickerCard : UserControl, IPathControl
{
    public FrameworkElement View => this;

    public static readonly DependencyProperty PathProperty =
        DependencyProperty.Register(
            nameof(Path), typeof(string), typeof(FolderPickerCard),
            new PropertyMetadata(string.Empty, OnPathOrDefaultChanged));

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public static readonly DependencyProperty DefaultPathProperty =
        DependencyProperty.Register(
            nameof(DefaultPath), typeof(string), typeof(FolderPickerCard),
            new PropertyMetadata(string.Empty, OnPathOrDefaultChanged));

    public string DefaultPath
    {
        get => (string)GetValue(DefaultPathProperty);
        set => SetValue(DefaultPathProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(
            nameof(Mode), typeof(FolderPickerMode), typeof(FolderPickerCard),
            new PropertyMetadata(FolderPickerMode.Configure, OnModeChanged));

    public FolderPickerMode Mode
    {
        get => (FolderPickerMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public event EventHandler? PathChanged;

    public FolderPickerCard()
    {
        InitializeComponent();
        RefreshDisplay();
        ApplyMode();
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderPickerCard card) card.ApplyMode();
    }

    private void ApplyMode()
    {
        PickButton.Visibility =
            Mode == FolderPickerMode.OpenOnly ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnPathOrDefaultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderPickerCard card) card.RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        string effective = string.IsNullOrEmpty(Path) ? (DefaultPath ?? string.Empty) : Path;
        PathTextBlock.Text = effective;
        PathTextBlock.Opacity = string.IsNullOrEmpty(Path) ? 0.6 : 1.0;
    }

    // Use the Windows App SDK picker (Microsoft.Windows.Storage.Pickers, 1.7+),
    // never the UWP Windows.Storage.Pickers one: the UWP picker does not work
    // under elevation and needs the InitializeWithWindow interop.
    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = SettingsHost.GetSettingsWindow?.Invoke()
                ?? throw new InvalidOperationException("Settings window not initialized");

            // The picker's Win32 dialog HWND is not exposed by the API, so its
            // position and size are unknowable: the probe records the trigger
            // button's rect as the anchoring intent, dialog rect at zero.
            EmitFolderPickerAnchor(sender as FrameworkElement, window);

            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
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
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
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

    // An empty Path opens DefaultPath: that is where the data actually lands,
    // not a placeholder.
    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        string target = string.IsNullOrEmpty(Path) ? (DefaultPath ?? string.Empty) : Path;
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
