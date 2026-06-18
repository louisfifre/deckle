using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// ── FolderPickerCard ────────────────────────────────────────────────────────
//
// Read-only display of a folder path with two actions: Change (opens the
// FolderPicker) and Show (opens the path in Explorer). Designed to live
// inside a SettingsCard at the consumer site — that lets the SettingsCard
// own Header / Description / HeaderIcon via x:Uid, and keeps the visual
// tree compatible with SettingsExpander.Items (which rejects UserControl
// wrappers around SettingsCard children).
//
// Path is exposed as a TwoWay DependencyProperty bound to a ViewModel
// property; auto-save handles persistence. When Path is empty, the
// TextBlock falls back to DefaultPath in the same secondary styling at
// reduced opacity — same UX as the legacy TextBox PlaceholderText, but
// without the misleading affordance of an editable input field.
public sealed partial class FolderPickerCard : UserControl
{
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

    // Picker affordance set. Configure shows both Change + Open; OpenOnly hides
    // the Change button so a folder the app owns reads as fixed (the path is
    // still shown and openable, just not repointable). Default Configure keeps
    // the existing call sites — which never set Mode — behaving as before.
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

    // Collapse the Change button in OpenOnly so the grid's auto column folds
    // away and only Open remains; Configure restores it. Driven both from the
    // constructor and on Mode changes so a programmatic set after construction
    // (the composer's Path case) takes effect.
    private void ApplyMode()
    {
        PickButton.Visibility =
            Mode == FolderPickerMode.OpenOnly ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnPathOrDefaultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderPickerCard card) card.RefreshDisplay();
    }

    // The TextBlock displays Path when set, otherwise falls back to
    // DefaultPath in the same secondary brush. Opacity dials down when
    // showing the fallback so it reads as a placeholder rather than a
    // real value.
    private void RefreshDisplay()
    {
        string effective = string.IsNullOrEmpty(Path) ? (DefaultPath ?? string.Empty) : Path;
        PathTextBlock.Text = effective;
        PathTextBlock.Opacity = string.IsNullOrEmpty(Path) ? 0.6 : 1.0;
    }

    // FolderPicker (WindowsAppSDK 1.7+ namespace Microsoft.Windows.Storage.Pickers).
    // Takes the AppWindow.Id of the parent Settings window via SettingsHost,
    // avoiding the legacy WinRT.Interop.InitializeWithWindow dance and the
    // elevation breakage that comes with the UWP-heritage picker.
    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = SettingsHost.GetSettingsWindow?.Invoke()
                ?? throw new InvalidOperationException("Settings window not initialized");

            // Windowing: system picker. Microsoft.Windows.Storage.Pickers
            // opens a Win32 COM dialog whose HWND the app does not own (the API
            // does not expose it); effective dialog pos/size are inaccessible
            // from code. Emit PopupAnchored with the button rect that triggered
            // the picker (UI Settings anchoring intent), dialog pos/size at
            // zero. parent_rect in absolute screen pixels computed through
            // TransformToVisual(null) + AppWindow.Position + scale DPI.
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
            DeckleSettingsSource.Log.FolderPickerFailed();
            DeckleSettingsSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // Computes the trigger button rect in absolute screen pixels from
    // TransformToVisual(null) (button position in the window in DIPs) +
    // AppWindow.Position (window top-left in screen pixels) + DPI scale. Reused
    // by both FolderPicker variants.
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

    // Open the effective path in Explorer. We open the fallback DefaultPath
    // when Path is empty — that's the actual location data lands in (e.g.
    // <UserDataRoot>\telemetry\), not a placeholder.
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
            DeckleSettingsSource.Log.FolderPickerFailed();
            DeckleSettingsSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }
}
