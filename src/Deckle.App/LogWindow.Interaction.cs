// LogWindow — pointer copy/drag-select, clipboard, and save.

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
    // ── Click-to-copy + drag-to-select + floating badge ────────────────────
    //
    // Hover: "Copy" badge to the right of the hovered line.
    // Simple click (press+release): copies 1 line, "Copied" feedback.
    // Click+drag: visual selection (Extended) of traversed lines,
    //   "Copy selection" badge, copies on release, deselects.

    private bool _isDragging;
    private int _dragStartIndex = -1;

    private void OnLogPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(LogItems).Properties.IsLeftButtonPressed) return;

        // Ignore presses that originate from the internal ScrollBar of the
        // ListView. Without this, dragging the scrollbar thumb bubbles a
        // PointerPressed up to the ListView, starts drag-select, and items
        // traversed during the drag get selected + copied on release.
        if (IsFromScrollBar(e.OriginalSource as DependencyObject)) return;

        _isDragging = true;
        var localY = e.GetCurrentPoint(LogItems).Position.Y;
        var container = FindContainerAtY(localY);
        _dragStartIndex = container?.Content is LogEntry ev
            ? _visible.IndexOf(ev)
            : -1;
    }

    private static bool IsFromScrollBar(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar) return true;
            source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OnLogPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var localY = e.GetCurrentPoint(LogItems).Position.Y;
        var container = FindContainerAtY(localY);

        if (container is null)
        {
            if (!_isDragging) CopyBadge.Visibility = Visibility.Collapsed;
            return;
        }

        // Position badge to the right of the item under the pointer.
        var transform = container.TransformToVisual(LogSurface);
        var pos = transform.TransformPoint(default);
        CopyBadgeTransform.Y = pos.Y + (container.ActualHeight - CopyBadge.ActualHeight) / 2;
        CopyBadge.Visibility = Visibility.Visible;

        if (_isDragging && _dragStartIndex >= 0 && container.Content is LogEntry currentEntry)
        {
            int currentIndex = _visible.IndexOf(currentEntry);
            if (currentIndex >= 0)
            {
                int start = Math.Min(_dragStartIndex, currentIndex);
                int end = Math.Max(_dragStartIndex, currentIndex);

                // Native visual selection via SelectRange (Extended mode).
                LogItems.DeselectRange(new ItemIndexRange(0, (uint)_visible.Count));
                LogItems.SelectRange(new ItemIndexRange(start, (uint)(end - start + 1)));

                CopyBadgeText.Text = Loc.Get((end > start) ? "LogWindow_CopyBadge_Selection" : "LogWindow_CopyBadge_Single");
            }
        }
        else if (!_isDragging)
        {
            if (CopyBadgeText.Text != Loc.Get("LogWindow_CopyBadge_Copied")) CopyBadgeText.Text = Loc.Get("LogWindow_CopyBadge_Single");
        }
    }

    private void OnLogPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;

        // Copy all selected lines in display order.
        var selected = LogItems.SelectedItems;
        if (selected.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var item in _visible)
            {
                if (selected.Contains(item))
                    sb.AppendLine(item.Text);
            }
            if (CopyToClipboard(sb.ToString()))
                ShowCopiedFeedback();
            else
                ShowCopyFailedFeedback();
        }

        // Full deselection — nothing persists.
        if (_visible.Count > 0)
            LogItems.DeselectRange(new ItemIndexRange(0, (uint)_visible.Count));
        _dragStartIndex = -1;
    }

    private void OnLogPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            CopyBadge.Visibility = Visibility.Collapsed;
        }
    }

    private ListViewItem? FindContainerAtY(double localY)
    {
        _itemsPanel ??= FindDescendant<ItemsStackPanel>(LogItems);
        if (_itemsPanel is null) return null;

        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(_itemsPanel);
        for (int i = 0; i < count; i++)
        {
            if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(_itemsPanel, i) is ListViewItem lvi)
            {
                var transform = lvi.TransformToVisual(LogItems);
                var pos = transform.TransformPoint(default);
                if (localY >= pos.Y && localY < pos.Y + lvi.ActualHeight)
                    return lvi;
            }
        }
        return null;
    }

    // Copy through the shared verified Win32 writer (Deckle.Core.
    // Win32Clipboard). The WinRT DataPackage/Clipboard.SetContent path this
    // replaced wrote unverified and relied on delayed rendering, which
    // truncated or failed silently on large selections — the bug this fixes.
    // Returns true when the bytes reached the OS clipboard; callers surface a
    // failure on the badge.
    private bool CopyToClipboard(string text)
    {
        ClipboardWriteResult r = Win32Clipboard.TryCopyText(text);
        if (!r.Landed)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail($"copy failed: {r.Status} ({r.ExpectedChars} chars)");
            return false;
        }
        if (r.Status is ClipboardWriteStatus.VerifyMissing or ClipboardWriteStatus.VerifyLengthMismatch)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"copy verify {r.Status}: expected {r.ExpectedChars}, clipboard {r.ActualChars}");
        }
        return true;
    }

    private async void ShowCopiedFeedback()
    {
        CopyBadgeText.Text = Loc.Get("LogWindow_CopyBadge_Copied");
        CopyBadge.Visibility = Visibility.Visible;
        await Task.Delay(800);
        if (CopyBadgeText.Text == Loc.Get("LogWindow_CopyBadge_Copied"))
            CopyBadgeText.Text = Loc.Get("LogWindow_CopyBadge_Single");
    }

    // Transient failure toast on the same badge. Unlike ShowCopiedFeedback it
    // collapses the badge afterwards — a failed copy from the CommandBar button
    // has no hovered row keeping the badge alive, so it must clear itself.
    private async void ShowCopyFailedFeedback()
    {
        CopyBadgeText.Text = Loc.Get("LogWindow_CopyBadge_Failed");
        CopyBadge.Visibility = Visibility.Visible;
        await Task.Delay(1600);
        if (CopyBadgeText.Text == Loc.Get("LogWindow_CopyBadge_Failed"))
        {
            CopyBadge.Visibility = Visibility.Collapsed;
            CopyBadgeText.Text = Loc.Get("LogWindow_CopyBadge_Single");
        }
    }

    // ── Copy button (CommandBar) ─────────────────────────────────────────────

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        // Copy all visible entries. Silent on success — the button has no row
        // to anchor a badge to. On failure the badge surfaces it at the top of
        // the surface so the user knows the clipboard was not updated.
        var sb = new StringBuilder();
        foreach (var entry in _visible) sb.AppendLine(entry.Text);
        if (!CopyToClipboard(sb.ToString()))
        {
            CopyBadgeTransform.Y = 0;
            ShowCopyFailedFeedback();
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            // WinUI 3 unpackaged: the picker needs the parent HWND.
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.SuggestedFileName = $"whisp-logs-{DateTime.Now:yyyyMMdd-HHmmss}";
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var sb = new StringBuilder();
            foreach (var entry in _visible) sb.AppendLine(entry.Text);
            await FileIO.WriteTextAsync(file, sb.ToString());
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail($"save err: {ex.Message}");
        }
    }
}
