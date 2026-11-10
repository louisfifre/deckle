using System.Text.RegularExpressions;

namespace Deckle.Atelier;

// The darktable sidecars that belong to a photo, so they travel with it: to
// the Recycle Bin, or to its new name. darktable names them
// "<basename>.<extension>.xmp", and a duplicate's "<basename>_nn.<extension>.xmp"
// (darktable manual, "sidecar files > import"). A plain "<basename>.xmp", as
// other tools write it, is left alone: a raw and its JPEG share that name, so
// it does not belong to one photo.
public static partial class PhotoSidecars
{
    public static IReadOnlyList<string> Of(string photoPath)
    {
        string? folder = Path.GetDirectoryName(photoPath);
        if (folder is null || !Directory.Exists(folder)) return [];
        string name = Path.GetFileName(photoPath);
        string stem = Path.GetFileNameWithoutExtension(photoPath);
        string ext = Path.GetExtension(photoPath);

        var found = new List<string>();
        try
        {
            foreach (string candidate in Directory.EnumerateFiles(folder, stem + "*" + ext + ".xmp"))
            {
                string candidateName = Path.GetFileName(candidate);
                if (candidateName.Equals(name + ".xmp", StringComparison.OrdinalIgnoreCase)
                    || IsDuplicateOf(candidateName, stem, ext))
                    found.Add(candidate);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    // The sidecar's name once its photo is renamed: the photo's stem is
    // replaced, the duplicate number and the extensions are kept.
    public static string Renamed(string sidecarName, string oldPhotoName, string newPhotoName)
    {
        string oldStem = Path.GetFileNameWithoutExtension(oldPhotoName);
        string newStem = Path.GetFileNameWithoutExtension(newPhotoName);
        return newStem + sidecarName[oldStem.Length..];
    }

    private static bool IsDuplicateOf(string candidateName, string stem, string ext)
    {
        string tail = ext + ".xmp";
        if (!candidateName.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
            || !candidateName.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
            || candidateName.Length <= stem.Length + 1 + tail.Length)
            return false;
        return DuplicateNumber().IsMatch(candidateName[(stem.Length + 1)..^tail.Length]);
    }

    [GeneratedRegex(@"^\d{2,}$")]
    private static partial Regex DuplicateNumber();
}
