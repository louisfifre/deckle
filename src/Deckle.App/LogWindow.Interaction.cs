// LogWindow — selection-aware clipboard and save actions.

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using WinRT.Interop;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
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

    private void OnCopyClick(object sender, RoutedEventArgs e)
        => CopySelectionOrVisible();

    private void OnCopyKeyboardAcceleratorInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs e)
    {
        CopySelectionOrVisible();
        e.Handled = true;
    }

    private void CopySelectionOrVisible()
    {
        HashSet<LogEntry>? selected = LogItems.SelectedItems.Count == 0
            ? null
            : LogItems.SelectedItems.OfType<LogEntry>().ToHashSet();

        var sb = new StringBuilder();
        foreach (LogEntry entry in _visible)
            if (selected is null || selected.Contains(entry))
                sb.AppendLine(entry.Text);

        if (sb.Length > 0)
            CopyToClipboard(sb.ToString());
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
