using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// Maps a path to the icon a list shows for it (spec, File list: every file
// shows an icon for its type). One seam, so that the icons Windows itself
// draws can replace the neutral glyphs without touching a tool (spec, Open 31:
// where the icons come from is not looked up yet).
public interface IFileIconProvider
{
    IconSource For(string path, bool isFolder);
}

// For now: one glyph for every folder, one for every file.
internal sealed class NeutralFileIconProvider : IFileIconProvider
{
    // IconSource describes an icon; each IconSourceElement builds its own
    // element from it, so one instance serves every row.
    // FontSize matches the 16 px cell the list gives an icon; the default
    // (20) draws past the cell and the glyph is cut on the right.
    private readonly FontIconSource _folder = new() { Glyph = AtelierGlyphs.Folder, FontSize = 16 };
    private readonly FontIconSource _file = new() { Glyph = AtelierGlyphs.File, FontSize = 16 };

    public IconSource For(string path, bool isFolder) => isFolder ? _folder : _file;
}
