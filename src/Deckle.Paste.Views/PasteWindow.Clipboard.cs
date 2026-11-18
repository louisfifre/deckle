using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Deckle.Paste.Views;

// The paste: what the clipboard holds, read once, handed to the saver off the
// UI thread, and the status line set from how it ended.
public sealed partial class PasteWindow
{
    private readonly PasteSaver _saver = new();
    private bool _pasting;

    // Ctrl+V (and Shift+Insert, its old twin) at the window, whatever holds
    // the focus. PreviewKeyDown, not a KeyboardAccelerator: the tunnelling
    // event reaches the root before the focused element, so the zone's own
    // TextBox never sees the keys, and no control can take them first.
    private void OnRootPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.KeyStatus.WasKeyDown) return;
        bool ctrl = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);
        bool alt = IsDown(VirtualKey.Menu);

        bool paste = (e.Key == VirtualKey.V && ctrl && !shift && !alt)
                  || (e.Key == VirtualKey.Insert && shift && !ctrl && !alt);
        if (!paste) return;

        e.Handled = true;
        _ = PasteAsync();
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPasteMenuClick(object sender, RoutedEventArgs e) => _ = PasteAsync();

    private async Task PasteAsync()
    {
        // One paste at a time: a second Ctrl+V during a copy of large files
        // waits for nothing and saves nothing.
        if (_pasting) return;
        _pasting = true;
        try
        {
            ShowStatus(PasteStatus.Saving, null);

            PasteContent? content;
            try
            {
                content = await ReadClipboardAsync();
            }
            catch (Exception)
            {
                // Another process holds the clipboard open, its data is gone, or
                // its bitmap does not decode (the codec throws a bare Exception
                // carrying an HRESULT). Any of them must end the "Saving" line:
                // this task is not awaited, so an escaped exception would leave
                // the ring spinning and a later paste refused.
                ShowStatus(PasteStatus.Failed, PasteText.Get("Paste_Error_ClipboardUnreadable"));
                return;
            }

            if (content is null)
            {
                ShowStatus(PasteStatus.Failed, PasteText.Get("Paste_Error_NothingToSave"));
                return;
            }

            string? folder = _folderStore.Folder;
            PasteSaveResult result = await Task.Run(() => _saver.Save(content, folder));
            if (result.Succeeded) ShowStatus(PasteStatus.Done, DescribeSaved(result.SavedPaths));
            else ShowStatus(PasteStatus.Failed, PasteText.ForSaveError(result.Error));
        }
        finally
        {
            _pasting = false;
        }
    }

    // What the clipboard holds, by priority: copied files, then an image, then
    // text. Null when it holds none of them. Read on the UI thread: the
    // clipboard belongs to the thread that owns the window.
    private static async Task<PasteContent?> ReadClipboardAsync()
    {
        DataPackageView view = Clipboard.GetContent();

        if (view.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
            string[] paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (paths.Length > 0) return new PastedFiles(paths);
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            RandomAccessStreamReference reference = await view.GetBitmapAsync();
            return new PastedImage(await EncodePngAsync(reference));
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            string text = await view.GetTextAsync();
            if (!string.IsNullOrEmpty(text)) return new PastedText(text);
        }

        return null;
    }

    // The clipboard's bitmap, decoded and written again as PNG, in memory.
    // Straight alpha keeps a transparent image transparent; a 32-bit DIB
    // without alpha decodes as opaque BGR, so a screenshot stays opaque.
    private static async Task<byte[]> EncodePngAsync(RandomAccessStreamReference reference)
    {
        using IRandomAccessStreamWithContentType source = await reference.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(source);
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);

        using var target = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[target.Size];
        using var reader = new DataReader(target.GetInputStreamAt(0));
        await reader.LoadAsync((uint)target.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    // One file by its name, several by their count.
    private static string? DescribeSaved(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => null,
        1 => Path.GetFileName(paths[0]),
        _ => PasteText.Format("Paste_Saved_Files_Format", paths.Count),
    };
}
