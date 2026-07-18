// LogWindow — selection, transfer scopes, clipboard and save actions.

using Deckle.Core;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private bool HasActiveFilter =>
        _filterSelection.Count > 0 || !string.IsNullOrEmpty(_currentSearch);

    private bool CopyToClipboard(string text)
    {
        ClipboardWriteResult result = Win32Clipboard.TryCopyText(text);
        if (!result.Landed)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"copy failed: {result.Status} ({result.ExpectedChars} chars)");
            return false;
        }

        if (result.Status is ClipboardWriteStatus.VerifyMissing
            or ClipboardWriteStatus.VerifyLengthMismatch)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail(
                $"copy verify {result.Status}: expected {result.ExpectedChars}, clipboard {result.ActualChars}");
        }

        return true;
    }

    private IReadOnlyList<LogEntry> ResolveTransferEntries(LogTransferScope scope)
    {
        var selected = LogItems.SelectedItems
            .OfType<LogEntry>()
            .ToHashSet();

        return LogTransferScopeResolver.Resolve(
            scope,
            _entries,
            Matches,
            selected);
    }

    private string BuildTransferText(LogTransferScope scope)
        => LogTransferText.Format(
            ResolveTransferEntries(scope),
            static entry => entry.Text);

    private void Copy(LogTransferScope scope)
    {
        string text = BuildTransferText(scope);
        if (text.Length > 0)
            CopyToClipboard(text);
    }

    private void OnCopyAllClick(SplitButton sender, SplitButtonClickEventArgs e)
        => Copy(LogTransferScope.All);

    private void OnCopySelectionClick(object sender, RoutedEventArgs e)
        => Copy(LogTransferScope.Selection);

    private void OnCopyFilteredClick(object sender, RoutedEventArgs e)
        => Copy(LogTransferScope.Filtered);

    private void OnCopyKeyboardAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs e)
    {
        // Ctrl+C is intentionally selection-only. With no selected row the
        // resolver returns an empty snapshot and the clipboard is untouched.
        Copy(LogTransferScope.Selection);
        e.Handled = true;
    }

    private async void OnSaveAllClick(SplitButton sender, SplitButtonClickEventArgs e)
        => await SaveAsync(LogTransferScope.All);

    private async void OnSaveSelectionClick(object sender, RoutedEventArgs e)
        => await SaveAsync(LogTransferScope.Selection);

    private async void OnSaveFilteredClick(object sender, RoutedEventArgs e)
        => await SaveAsync(LogTransferScope.Filtered);

    private async Task SaveAsync(LogTransferScope scope)
    {
        // Snapshot before opening the picker: filter changes or incoming events
        // while the modal UI is open cannot silently change what gets written.
        string text = BuildTransferText(scope);
        if (text.Length == 0) return;

        try
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, _hwnd);
            picker.SuggestedFileName = $"deckle-logs-{DateTime.Now:yyyyMMdd-HHmmss}";
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is not null)
                await FileIO.WriteTextAsync(file, text);
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.LogWindowWarning();
            DeckleAppSource.Log.LogWindowWarningDetail($"save err: {ex.Message}");
        }
    }

    private void OnLogItemsRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var item = FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (item is null || item.IsSelected) return;

        LogItems.SelectedItems.Clear();
        item.IsSelected = true;
        item.Focus(FocusState.Programmatic);
    }

    private void OnLogContextFlyoutOpening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout ||
            flyout.Items.FirstOrDefault() is not MenuFlyoutItem copyItem)
        {
            return;
        }

        copyItem.Text = LogItems.SelectedItems.Count switch
        {
            1 => Loc.Get("LogWindow_ContextCopy_One"),
            > 1 => Loc.Get("LogWindow_ContextCopy_Selection"),
            _ when HasActiveFilter => Loc.Get("LogWindow_ContextCopy_Filtered"),
            _ => Loc.Get("LogWindow_ContextCopy_All"),
        };
    }

    private void OnContextCopyClick(object sender, RoutedEventArgs e)
    {
        LogTransferScope scope = LogItems.SelectedItems.Count > 0
            ? LogTransferScope.Selection
            : HasActiveFilter
                ? LogTransferScope.Filtered
                : LogTransferScope.All;

        Copy(scope);
    }

    private static T? FindAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T found) return found;
            child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
