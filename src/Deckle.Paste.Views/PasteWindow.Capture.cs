using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Deckle.Paste.Views;

// What the self-check captures: the inset empty, with a short list, with a
// problem above a saved row, and full enough to scroll. Every row, name and
// excerpt is invented, the thumbnails are drawn here in code, and the folder
// shown is an example: no state reads the clipboard, the disk or the store.
public sealed partial class PasteWindow
{
    public static IReadOnlyList<string> CaptureStates { get; } = ["empty", "list", "failed", "full"];

    private const string SampleFolder = @"C:\Users\Public\Documents\Inbox";

    public void ShowStateForCapture(string state)
    {
        if (!CaptureStates.Contains(state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown capture state.");

        ClearList();
        ShowFolder(SampleFolder);
        switch (state)
        {
            case "list":
                AddPaste([PasteEntry.File(
                    "Quarterly planning workshop summary with appendices and final review comments.docx",
                    PasteFileKind.Document)]);
                AddPaste([PasteEntry.Text("paste-2026-11-18-142940.txt",
                    "Shopping list for the weekend:\r\nflour, eggs, two lemons, a bag of coffee beans and some fresh basil")]);
                AddPaste([PasteEntry.Image("paste-2026-11-18-143005.png")], SampleThumbnail(Sunrise));
                break;
            case "failed":
                AddPaste([PasteEntry.Image("paste-2026-11-18-143005.png")], SampleThumbnail(Sunrise));
                AddPaste([PasteEntry.Problem(PasteEntryKind.NotSaved, PasteSaveErrors.NothingToSave)]);
                break;
            case "full":
                AddPaste([PasteEntry.Image("paste-2026-11-18-140112.png")], SampleThumbnail(Lagoon));
                AddPaste([
                    PasteEntry.File("Product demo recording.mp4", PasteFileKind.Video),
                    PasteEntry.File("Interview take 2.m4a", PasteFileKind.Audio),
                    PasteEntry.File("Reference photos", PasteFileKind.Folder),
                    PasteEntry.File("Mood board.jpg", PasteFileKind.Image),
                    PasteEntry.File("settings-backup.dat", PasteFileKind.Other),
                ]);
                AddPaste([PasteEntry.Text("paste-2026-11-18-142940.txt",
                    "Remember to water the plants on the balcony before leaving on Friday")]);
                AddPaste([PasteEntry.Image("paste-2026-11-18-143005.png")], SampleThumbnail(Sunrise));
                break;
        }
    }

    // Two invented pictures, so the thumbnails of one capture differ.
    private static readonly (uint Top, uint Bottom, uint Disc) Sunrise = (0xFF2B4C7E, 0xFFF2A65A, 0xFFFFE8B0);
    private static readonly (uint Top, uint Bottom, uint Disc) Lagoon = (0xFF0E5E6F, 0xFF7FD1C7, 0xFFFFFFFF);

    // A small square picture drawn pixel by pixel: a vertical gradient with
    // a disc, enough to see the thumbnail's fill, corners and crop.
    private static ImageSource SampleThumbnail((uint Top, uint Bottom, uint Disc) colors)
    {
        const int size = 64;
        var bitmap = new WriteableBitmap(size, size);
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double t = y / (double)(size - 1);
            bool disc = Math.Pow(x - size * 0.62, 2) + Math.Pow(y - size * 0.4, 2) < Math.Pow(size * 0.16, 2);
            uint color = disc ? colors.Disc : Blend(colors.Top, colors.Bottom, t);
            int i = (y * size + x) * 4;
            pixels[i] = (byte)color;              // B
            pixels[i + 1] = (byte)(color >> 8);   // G
            pixels[i + 2] = (byte)(color >> 16);  // R
            pixels[i + 3] = 0xFF;                 // A, opaque
        }
        using (Stream stream = bitmap.PixelBuffer.AsStream())
            stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    private static uint Blend(uint a, uint b, double t)
    {
        uint Channel(int shift) =>
            (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t) << shift;
        return 0xFF000000 | Channel(16) | Channel(8) | Channel(0);
    }
}
