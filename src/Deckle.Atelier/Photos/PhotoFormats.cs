namespace Deckle.Atelier;

// Which files the photo tool counts as photos, by extension.
//
// Source: darktable's own list of what it imports, since the photo tool hands
// the folder over to darktable (data/supported_extensions.cmake on darktable's
// master branch, plus the formats src/CMakeLists.txt adds when a build carries
// the library for them: cr3, heif/heic/hif, avif, webp, jxl). Read on
// 2026-11-10.
//
// Kept from that list: what a camera or a phone writes, the raw formats, JPEG,
// TIFF and the HEIF family. Left out on purpose: the formats darktable opens
// that are intermediates or graphics rather than shots (png, gif, bmp, exr,
// hdr, pfm, the netpbm family, qoi, JPEG 2000, fits, dpx). A screenshot in a
// shoot folder therefore stays a file, not a photo.
public static class PhotoFormats
{
    // Camera raw formats from darktable's base list (rawspeed and LibRaw
    // containers), plus cr3, which the official builds carry.
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".ari", ".arw", ".bay", ".bmq", ".cap", ".cine", ".cr2", ".cr3", ".crw",
        ".cs1", ".dc2", ".dcr", ".dng", ".erf", ".fff", ".gpr", ".ia", ".iiq", ".k25",
        ".kc2", ".kdc", ".mdc", ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf", ".ori",
        ".pef", ".pxn", ".qtk", ".raf", ".raw", ".rdc", ".rw2", ".rwl", ".sr2", ".srf",
        ".srw", ".sti", ".x3f",
    };

    private static readonly HashSet<string> DevelopedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".tif", ".tiff", ".heic", ".heif", ".hif", ".avif", ".webp", ".jxl",
    };

    public static IReadOnlyCollection<string> Raw => RawExtensions;

    public static IReadOnlyCollection<string> Developed => DevelopedExtensions;

    public static bool IsPhoto(string path)
    {
        string ext = Path.GetExtension(path);
        return RawExtensions.Contains(ext) || DevelopedExtensions.Contains(ext);
    }

    public static bool IsRaw(string path) => RawExtensions.Contains(Path.GetExtension(path));
}
