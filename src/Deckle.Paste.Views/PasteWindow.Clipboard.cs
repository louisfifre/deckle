using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;

namespace Deckle.Paste.Views;

// The paste: what the clipboard holds, read once, handed to the saver off the
// UI thread, and the list given the rows of how it ended.
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

    // What the clipboard gave: the content to save and, for an image, its
    // row's preview, already scaled down.
    private sealed record ClipboardRead(PasteContent Content, SoftwareBitmap? Thumbnail);

    private async Task PasteAsync()
    {
        // One paste at a time: a second Ctrl+V during a copy of large files
        // waits for nothing and saves nothing.
        if (_pasting) return;
        _pasting = true;
        try
        {
            ShowSaving(true);

            ClipboardRead? read;
            try
            {
                read = await ReadClipboardAsync(Content.XamlRoot?.RasterizationScale ?? 1.0);
            }
            catch (Exception ex)
            {
                // Another process holds the clipboard open, its data is gone, or
                // its bitmap does not decode (the codec throws a bare Exception
                // carrying an HRESULT). Any of them must end the saving sign:
                // this task is not awaited, so an escaped exception would leave
                // the ring spinning and a later paste refused.
                PasteWindowLog.ClipboardUnreadable(ex);
                ShowSaving(false);
                AddPaste([PasteEntry.Problem(PasteEntryKind.NotSaved, PasteText.Get("Paste_Error_ClipboardUnreadable"))]);
                return;
            }

            if (read is null)
            {
                PasteWindowLog.NothingToSave();
                ShowSaving(false);
                AddPaste([PasteEntry.Problem(PasteEntryKind.NotSaved, PasteText.Get("Paste_Error_NothingToSave"))]);
                return;
            }

            string? folder = _folderStore.Folder;
            PasteContent content = read.Content;
            // The rows are built off the UI thread too: telling a copied
            // folder from a file asks the disk.
            (PasteSaveResult result, IReadOnlyList<PasteEntry> rows) = await Task.Run(() =>
            {
                PasteSaveResult saved = _saver.Save(content, folder);
                return (saved, PasteHistory.RowsFor(content, saved));
            });

            ImageSource? thumbnail = result.Succeeded
                ? await ToImageSourceAsync(read.Thumbnail)
                : DisposeThumbnail(read.Thumbnail);
            ShowSaving(false);
            AddPaste(rows, thumbnail);
        }
        finally
        {
            _pasting = false;
            ShowSaving(false);
        }
    }

    private static ImageSource? DisposeThumbnail(SoftwareBitmap? thumbnail)
    {
        thumbnail?.Dispose();
        return null;
    }

    // What the clipboard holds, by priority: copied files, then an image, then
    // text. Null when it holds none of them. Read on the UI thread: the
    // clipboard belongs to the thread that owns the window.
    private static async Task<ClipboardRead?> ReadClipboardAsync(double rasterizationScale)
    {
        DataPackageView view = Clipboard.GetContent();

        if (view.Contains(StandardDataFormats.StorageItems))
        {
            IReadOnlyList<IStorageItem> items = await view.GetStorageItemsAsync();
            string[] paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (paths.Length > 0) return new ClipboardRead(new PastedFiles(paths), null);
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            RandomAccessStreamReference reference = await view.GetBitmapAsync();
            return await EncodePngAsync(reference, rasterizationScale);
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            string text = await view.GetTextAsync();
            if (!string.IsNullOrEmpty(text)) return new ClipboardRead(new PastedText(text), null);
        }

        return null;
    }

    // The clipboard's bitmap, decoded and written again as PNG, in memory,
    // with its row's preview drawn from the same decoder. Straight alpha keeps
    // a transparent image transparent; a 32-bit DIB without alpha decodes as
    // opaque BGR, so a screenshot stays opaque.
    private static async Task<ClipboardRead> EncodePngAsync(RandomAccessStreamReference reference, double rasterizationScale)
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

        SoftwareBitmap? thumbnail = await DecodeThumbnailAsync(decoder, rasterizationScale);
        return new ClipboardRead(new PastedImage(bytes), thumbnail);
    }
}
