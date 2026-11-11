namespace Deckle.Atelier;

// The few path rules every part of the Atelier must agree on. Windows paths
// compare without case, a folder is written without a trailing separator
// except a drive root ("D:\"), and "inside" means a whole segment matches:
// D:\Photos2 is not inside D:\Photos.
public static class FolderPaths
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        return System.IO.Path.TrimEndingDirectorySeparator(full);
    }

    public static bool AreSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    // True when path is root itself or sits beneath it.
    public static bool IsInside(string root, string path)
    {
        string r = Normalize(root);
        string p = Normalize(path);
        if (string.Equals(r, p, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = r.EndsWith(System.IO.Path.DirectorySeparatorChar) ? r : r + System.IO.Path.DirectorySeparatorChar;
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    // The folder's own name; a drive root has none, so it shows as "D:".
    public static string DisplayName(string path)
    {
        string n = Normalize(path);
        string name = System.IO.Path.GetFileName(n);
        return name.Length > 0 ? name : n.TrimEnd(System.IO.Path.DirectorySeparatorChar);
    }
}
