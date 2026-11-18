namespace Deckle.Paste;

// What a saved file is, read from its extension alone: the row only picks an
// icon, so a file is never opened to tell.
public static class PasteFileKinds
{
    private static readonly Dictionary<string, PasteFileKind> ByExtension = Build();

    public static PasteFileKind Of(string name, bool isFolder)
    {
        if (isFolder) return PasteFileKind.Folder;
        string extension = Path.GetExtension(name);
        return ByExtension.TryGetValue(extension, out PasteFileKind kind) ? kind : PasteFileKind.Other;
    }

    private static Dictionary<string, PasteFileKind> Build()
    {
        var map = new Dictionary<string, PasteFileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(PasteFileKind kind, params string[] extensions)
        {
            foreach (string e in extensions) map[e] = kind;
        }
        Add(PasteFileKind.Image, ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
            ".heic", ".heif", ".avif", ".ico", ".svg", ".jxl");
        Add(PasteFileKind.Video, ".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg");
        Add(PasteFileKind.Audio, ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma");
        Add(PasteFileKind.Document, ".txt", ".md", ".pdf", ".rtf", ".doc", ".docx", ".odt", ".xls", ".xlsx",
            ".ods", ".csv", ".ppt", ".pptx", ".odp");
        return map;
    }
}
