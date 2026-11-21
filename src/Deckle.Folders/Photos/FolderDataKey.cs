using System.Security.Cryptography;
using System.Text;

namespace Deckle.Folders;

// The file name a folder's own data takes in the module's store (its marks,
// its descriptions): first 16 hex digits of SHA-256 over the lower-cased
// path. Stable across runs, unlike string.GetHashCode, and short enough for a
// file name; the case is ignored as NTFS ignores it.
internal static class FolderDataKey
{
    public static string Of(string normalizedFolder) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedFolder.ToLowerInvariant())))[..16].ToLowerInvariant();
}
