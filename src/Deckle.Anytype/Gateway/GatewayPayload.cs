using System.Security.Cryptography;
using System.Text;

namespace Deckle.Anytype;

// The gateway's build output beside Deckle.exe, and its copy under the providers
// folder that the gateway really runs from.
//
// Why a copy: a process locks its own image and DLLs. Run from the build output,
// the gateway would make every rebuild of a shared library fail. Run from a copy,
// the build output stays free and the gateway keeps serving while Deckle rebuilds.
//
// Why a fingerprint: the copy's folder is named after a hash of the payload's
// content, so two builds of the same code share one folder and the supervisor
// replaces a running gateway only when the code it carries changed. This relies on
// deterministic builds, and on the libraries leaving the commit id out of their
// informational version (Directory.Build.props), or every commit would read as a
// new gateway.
internal static class GatewayPayload
{
    // Debug symbols do not change what runs; leaving them out keeps the
    // fingerprint about code alone.
    private static readonly string[] IgnoredExtensions = [".pdb"];

    private const string CompleteMarker = ".complete";
    private const int FingerprintLength = 16;

    // The hash of every file the gateway runs with, path and content, in a fixed
    // order. Null when the folder or the gateway executable is absent.
    public static string? Fingerprint(string payloadDirectory)
    {
        if (!File.Exists(Path.Combine(payloadDirectory, GatewayContract.ExecutableName)))
            return null;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string relative in Files(payloadDirectory))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative.ToLowerInvariant()));
            hash.AppendData([0]);
            hash.AppendData(File.ReadAllBytes(Path.Combine(payloadDirectory, relative)));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..FingerprintLength].ToLowerInvariant();
    }

    // Copies the payload into versionsRoot\<fingerprint> once and answers the
    // executable to run. A folder is used only once its marker exists, so a copy
    // cut short by a crash is never launched; it is rebuilt under a fresh staging
    // name and moved into place whole.
    public static string Stage(string payloadDirectory, string fingerprint, string versionsRoot)
    {
        string target = Path.Combine(versionsRoot, fingerprint);
        string executable = Path.Combine(target, GatewayContract.ExecutableName);
        if (File.Exists(Path.Combine(target, CompleteMarker)) && File.Exists(executable))
            return executable;

        Directory.CreateDirectory(versionsRoot);
        string staging = Path.Combine(versionsRoot, $".staging-{Guid.NewGuid():N}");
        try
        {
            foreach (string relative in Files(payloadDirectory))
            {
                string destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(payloadDirectory, relative), destination);
            }
            File.WriteAllText(Path.Combine(staging, CompleteMarker), fingerprint);

            // A leftover without its marker is a copy that never finished.
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
        }
        finally
        {
            if (Directory.Exists(staging))
                TryDelete(staging);
        }

        return executable;
    }

    // Removes every version folder except the one in use. The folder is renamed
    // before it is deleted: Windows refuses to rename a folder whose gateway still
    // runs, so a live version is never half deleted under its own process (a
    // recursive delete would remove the DLLs it has not loaded yet before failing
    // on its image). Such a folder stays until a later start.
    public static int Prune(string versionsRoot, string keepFingerprint)
    {
        if (!Directory.Exists(versionsRoot))
            return 0;

        int removed = 0;
        foreach (string folder in Directory.EnumerateDirectories(versionsRoot))
        {
            if (string.Equals(Path.GetFileName(folder), keepFingerprint, StringComparison.OrdinalIgnoreCase))
                continue;

            string doomed = Path.Combine(versionsRoot, $".removing-{Guid.NewGuid():N}");
            try
            {
                Directory.Move(folder, doomed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (TryDelete(doomed))
                removed++;
        }
        return removed;
    }

    // True when path is a gateway executable of any version under versionsRoot.
    public static bool IsStagedGateway(string path, string versionsRoot)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(versionsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(full), GatewayContract.ExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Files(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IgnoredExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.OrdinalIgnoreCase);

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
