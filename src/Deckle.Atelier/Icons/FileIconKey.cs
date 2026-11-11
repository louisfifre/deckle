namespace Deckle.Atelier;

// Which icons are the same picture. Every file of one type shows its type's
// icon, so a folder of five hundred Markdown notes reads the shell once per
// size. A few types draw an icon of their own per file: a program its
// embedded icon, a shortcut its target's, an icon or cursor file itself, an
// installer package its product's; and a folder can carry a custom icon
// (desktop.ini). Those are keyed by path.
//
// Subject is lower-cased: NTFS paths and extensions compare without case, so
// "Note.MD" and "note.md" share one entry.
public readonly record struct FileIconKey(string Subject, bool PerFile, int PixelSize)
{
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".cur", ".ani", ".msi",
    };

    public static FileIconKey For(string path, bool isFolder, int pixelSize)
    {
        if (isFolder) return new FileIconKey(path.ToLowerInvariant(), PerFile: true, pixelSize);
        string extension = Path.GetExtension(path);
        return PerFileExtensions.Contains(extension)
            ? new FileIconKey(path.ToLowerInvariant(), PerFile: true, pixelSize)
            : new FileIconKey(extension.ToLowerInvariant(), PerFile: false, pixelSize);
    }
}

// A thumbnail is the picture of one file's content, so it is keyed by the
// file, never by its type, and by the file's last write: a photo edited since
// it was last shown gets a new picture, not the remembered one.
public readonly record struct FileThumbnailKey(string Path, int PixelSize, long LastWriteUtcTicks)
{
    public static FileThumbnailKey For(string path, int pixelSize, DateTime lastWriteUtc) =>
        new(path.ToLowerInvariant(), pixelSize, lastWriteUtc.Ticks);
}
