namespace Deckle.Atelier;

// What a thumbnail request needs to know of the file before it asks the
// shell: whether it still exists, when it was last written (the cache key),
// and whether its content is on this disk.
public sealed record FileThumbnailProbe(DateTime LastWriteUtc, bool IsLocal)
{
    public static FileThumbnailProbe? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            return new FileThumbnailProbe(file.LastWriteTimeUtc, FileContentPresence.IsLocal(file.Attributes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
