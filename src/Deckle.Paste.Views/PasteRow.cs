using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Paste.Views;

// One row of the inset's list as the template binds it (PasteWindow.xaml):
// a PasteEntry with its words resolved and, for a pasted image, its
// thumbnail. A row is read, never pressed: no command, no selection.
// Public for the XAML compiler, which resolves an x:DataType only then.
public sealed class PasteRow
{
    // Segoe Fluent Icons.
    private const string GlyphImage = "\uE91B";    // Photo
    private const string GlyphVideo = "\uE714";    // Video
    private const string GlyphAudio = "\uE8D6";    // Audio
    private const string GlyphDocument = "\uE8A5"; // Document
    private const string GlyphFolder = "\uE8B7";   // Folder
    private const string GlyphFile = "\uE7C3";     // Page

    public PasteRow(PasteEntry entry, ImageSource? thumbnail = null)
    {
        Entry = entry;
        Thumbnail = entry.Kind == PasteEntryKind.Image ? thumbnail : null;

        if (entry.IsProblem)
        {
            Title = PasteText.Get(entry.Kind switch
            {
                PasteEntryKind.FolderNotChanged => "Paste_Row_FolderNotChanged",
                PasteEntryKind.FolderNotRemembered => "Paste_Row_FolderNotRemembered",
                _ => "Paste_Row_NotSaved",
            });
            Detail = PasteText.ForSaveError(entry.Detail);
        }
        else
        {
            Title = entry.Name;
            Detail = string.IsNullOrEmpty(entry.Detail) ? null : entry.Detail;
        }

        Glyph = entry.FileKind switch
        {
            PasteFileKind.Image => GlyphImage,
            PasteFileKind.Video => GlyphVideo,
            PasteFileKind.Audio => GlyphAudio,
            PasteFileKind.Document => GlyphDocument,
            PasteFileKind.Folder => GlyphFolder,
            _ => GlyphFile,
        };

        ThumbnailVisibility = Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
        ProblemVisibility = entry.IsProblem ? Visibility.Visible : Visibility.Collapsed;
        GlyphVisibility = Thumbnail is null && !entry.IsProblem ? Visibility.Visible : Visibility.Collapsed;
        DetailVisibility = Detail is null ? Visibility.Collapsed : Visibility.Visible;
        // The whole name, or the whole reason, for what the row trims.
        ToolTip = entry.IsProblem ? Detail : Title;
    }

    public PasteEntry Entry { get; }
    public ImageSource? Thumbnail { get; }
    public string Glyph { get; }
    public string Title { get; }
    public string? Detail { get; }
    public string? ToolTip { get; }
    public Visibility ThumbnailVisibility { get; }
    public Visibility GlyphVisibility { get; }
    public Visibility ProblemVisibility { get; }
    public Visibility DetailVisibility { get; }

    // What Narrator hears when the row arrives: the saved name, or the
    // problem and its reason.
    public string Spoken => Entry.IsProblem
        ? PasteText.Format("Paste_Announce_Problem_Format", Title, Detail)
        : Title;
}
