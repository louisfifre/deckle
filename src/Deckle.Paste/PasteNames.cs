using System.Globalization;

namespace Deckle.Paste;

// The names a paste is written under. A text or an image gets a name made of
// its moment; a copied file keeps its own. A name already taken in the folder
// gets " (2)", " (3)"... before its extension, the way Explorer names a copy,
// so nothing is ever overwritten.
internal static class PasteNames
{
    // "paste-2026-11-18-142305" for 14:23:05 on that day. The moment is read
    // in its own offset: the default clock is DateTimeOffset.Now, the local
    // time, and a test clock gives the clock time it states.
    public static string Generated(DateTimeOffset moment, string extension) =>
        "paste-" + moment.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + extension;

    // The candidate paths for this name in this folder, in the order they are
    // tried: the name itself, then "name (2).ext", "name (3).ext"... Only the
    // first that nothing occupies is ever written; the caller still writes
    // without overwriting, since another process can take a name in between.
    public static IEnumerable<string> Candidates(string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        yield return Path.Combine(folder, name);
        for (int n = 2; ; n++)
            yield return Path.Combine(folder, $"{stem} ({n}){extension}");
    }

    public static bool IsTaken(string path) => File.Exists(path) || Directory.Exists(path);
}
