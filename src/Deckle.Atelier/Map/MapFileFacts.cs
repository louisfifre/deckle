namespace Deckle.Atelier;

// What a recognition rule may ask of one file: where it sits, its name, and,
// read once and only when a rule needs it, its text and front matter.
internal sealed class MapFileFacts
{
    private readonly Func<string, bool> _exists;
    private readonly long _maxBytes;
    private string? _text;
    private bool _textRead;
    private FrontMatter? _frontMatter;

    public MapFileFacts(string fullPath, string relativePath, Func<string, bool> exists, long maxBytes)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        _exists = exists;
        _maxBytes = maxBytes;
        int slash = relativePath.LastIndexOf('/');
        Folder = slash < 0 ? "" : relativePath[..slash];
        FileName = relativePath[(slash + 1)..];
        Extension = System.IO.Path.GetExtension(FileName).ToLowerInvariant();
        Stem = System.IO.Path.GetFileNameWithoutExtension(FileName);
    }

    public string FullPath { get; }

    public string RelativePath { get; }

    // The folder that holds the file, relative; "" at the root.
    public string Folder { get; }

    public string FileName { get; }

    public string Extension { get; }

    public string Stem { get; }

    public bool TooLarge { get; private set; }

    public bool Unreadable { get; private set; }

    public bool Is(string relativePath) => string.Equals(RelativePath, relativePath, StringComparison.OrdinalIgnoreCase);

    public bool Named(string fileName) => string.Equals(FileName, fileName, StringComparison.OrdinalIgnoreCase);

    // Directly or deeper inside the folder at this relative path.
    public bool Under(string folder) => RelativePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    // Inside a folder of this name at any depth, the root included.
    public bool UnderAny(string folder) =>
        Under(folder) || RelativePath.Contains("/" + folder + "/", StringComparison.OrdinalIgnoreCase);

    public bool Exists(string relativePath) => _exists(relativePath);

    // Null when the file is over the size limit or cannot be read.
    public string? Text
    {
        get
        {
            if (_textRead) return _text;
            _textRead = true;
            try
            {
                if (new FileInfo(FullPath).Length > _maxBytes) TooLarge = true;
                else _text = File.ReadAllText(FullPath);
            }
            catch (IOException) { Unreadable = true; }
            catch (UnauthorizedAccessException) { Unreadable = true; }
            return _text;
        }
    }

    public FrontMatter FrontMatter => _frontMatter ??= Text is { } text ? FrontMatter.Parse(text) : FrontMatter.None;
}
