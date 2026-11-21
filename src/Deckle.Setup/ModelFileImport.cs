using System.Security.Cryptography;

namespace Deckle.Setup;

/// <summary>
/// Installs a model file picked by hand without ever leaving a half-written or
/// unverified file at the destination. The source is copied beside the
/// destination under a temporary name, the hash is checked on that copy, and only
/// then is the copy moved over the destination. Same method as
/// <c>DataRootTree.TryCopyFile</c>: a failure at any step leaves an installed
/// model untouched and no temporary file behind.
/// </summary>
internal static class ModelFileImport
{
    /// <summary>
    /// Returns true when <paramref name="destination"/> now holds the source;
    /// false when the hash of the copy differs from <paramref name="expectedSha256"/>
    /// (blank means no check). Exceptions from the copy or the move propagate,
    /// after the temporary file is removed.
    /// </summary>
    public static bool Install(string source, string destination, string? expectedSha256)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string partial = $"{destination}.deckle-{Guid.NewGuid():N}.partial";
        try
        {
            File.Copy(source, partial, overwrite: false);

            if (!string.IsNullOrWhiteSpace(expectedSha256)
                && !string.Equals(ComputeSha256(partial), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            File.Move(partial, destination, overwrite: true);
            return true;
        }
        finally
        {
            // After a successful move the temporary name no longer exists.
            TryDeletePartial(partial);
        }
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDeletePartial(string partial)
    {
        try { if (File.Exists(partial)) File.Delete(partial); }
        catch (Exception ex)
        {
            DeckleSetupSource.Log.BestEffortFailedDetail(
                "delete_partial", partial, ex.GetType().Name, ex.Message);
        }
    }
}
