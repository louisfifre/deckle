namespace Deckle.Browser;

// How a site's icon file is named: from its host, so every page of a site
// shares one icon. The views write and read the PNG bytes.
public static class FaviconCache
{
    public static string FileName(string host)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new([.. host.Trim().ToLowerInvariant().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)]);
        return (name.Length == 0 ? "_" : name) + ".png";
    }
}
