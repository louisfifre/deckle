using System.IO.Compression;
using System.Text.Json;

namespace Deckle.Anytype;

internal interface IBackendProviderCatalog
{
    BackendProcessSpec? ResolveActiveSpec();
    IReadOnlyList<string> TrustedExecutablePaths();
}

// Version directories are immutable. Activation is one small replaceable file,
// so an interrupted provisioning attempt can leave staging behind but can never
// expose a partially extracted provider as current.
internal sealed class BackendProviderStore(
    string providerDirectory,
    string legacyDirectory,
    string arguments = "serve --no-update-check",
    IBackendProviderPublicationCoordinator? publicationCoordinator = null) : IBackendProviderCatalog
{
    private const string ExecutableName = "anytype.exe";
    private readonly string _providerDirectory = Path.GetFullPath(providerDirectory);
    private readonly string _legacyDirectory = Path.GetFullPath(legacyDirectory);
    private readonly string _arguments = arguments;
    private readonly IBackendProviderPublicationCoordinator _publication =
        publicationCoordinator ?? new BackendProviderPublicationLease();

    internal string ProviderDirectory => _providerDirectory;
    internal string LegacyDirectory => _legacyDirectory;
    internal string LegacyExecutablePath => Path.Combine(_legacyDirectory, ExecutableName);
    internal string ActivationPath => Path.Combine(_providerDirectory, "active.json");
    internal string VersionDirectory(string version)
    {
        ValidateVersion(version);
        return Path.Combine(_providerDirectory, "versions", version);
    }

    public BackendProcessSpec? ResolveActiveSpec()
    {
        string? path = ResolveActiveExecutable();
        return path is not null ? new(path, _arguments) : null;
    }

    public IReadOnlyList<string> TrustedExecutablePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string versions = Path.Combine(_providerDirectory, "versions");
        if (Directory.Exists(versions))
        {
            foreach (string versionDirectory in Directory.EnumerateDirectories(
                         versions, "*", SearchOption.TopDirectoryOnly))
            {
                string executable = Path.Combine(versionDirectory, ExecutableName);
                if (File.Exists(executable)) paths.Add(Path.GetFullPath(executable));
            }
        }
        if (File.Exists(LegacyExecutablePath)) paths.Add(LegacyExecutablePath);
        return [.. paths];
    }

    internal bool IsInstalled() => ResolveActiveExecutable() is not null || File.Exists(LegacyExecutablePath);

    internal async Task<bool> MigrateLegacyAsync(string version, CancellationToken ct)
    {
        if (ResolveActiveExecutable() is not null || !File.Exists(LegacyExecutablePath))
            return ResolveActiveExecutable() is not null;

        string staging = NewStagingDirectory();
        string ready = staging + ".ready";
        try
        {
            await Task.Run(() => CopyDirectory(_legacyDirectory, ready, ct), ct).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(ready, ExecutableName))) return false;
            PublishReadyVersion(ready, version, ct);
            return true;
        }
        finally
        {
            TryDelete(staging);
            TryDelete(ready);
        }
    }

    internal async Task<bool> InstallFromZipAsync(
        string zipPath,
        string version,
        CancellationToken ct)
    {
        string staging = NewStagingDirectory();
        string ready = staging + ".ready";
        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(staging);
                ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: false);

                string? executable = Directory
                    .EnumerateFiles(staging, ExecutableName, SearchOption.AllDirectories)
                    .OrderBy(path => path.Count(c => c == Path.DirectorySeparatorChar))
                    .FirstOrDefault();
                if (executable is null) return;

                CopyDirectory(Path.GetDirectoryName(executable)!, ready, ct);
            }, ct).ConfigureAwait(false);

            if (!File.Exists(Path.Combine(ready, ExecutableName))) return false;
            PublishReadyVersion(ready, version, ct);
            return ResolveActiveExecutable() is not null;
        }
        finally
        {
            TryDelete(staging);
            TryDelete(ready);
        }
    }

    internal void Activate(string version)
    {
        _publication.Run(() => ActivateProtected(version), CancellationToken.None);
    }

    private void ActivateProtected(string version)
    {
        string executable = Path.Combine(VersionDirectory(version), ExecutableName);
        if (!File.Exists(executable))
            throw new FileNotFoundException("The provider version is incomplete.", executable);

        Directory.CreateDirectory(_providerDirectory);
        string relative = Path.GetRelativePath(_providerDirectory, executable);
        var manifest = new BackendActivation(version, relative.Replace('\\', '/'));
        string temporary = Path.Combine(
            _providerDirectory, $"active.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, manifest);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(ActivationPath))
                File.Replace(temporary, ActivationPath, destinationBackupFileName: null);
            else
                File.Move(temporary, ActivationPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private string? ResolveActiveExecutable()
    {
        if (!File.Exists(ActivationPath)) return null;
        try
        {
            BackendActivation? manifest = JsonSerializer.Deserialize<BackendActivation>(
                File.ReadAllText(ActivationPath));
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.RelativeExecutable)) return null;

            if (!IsValidVersion(manifest.Version)) return null;
            string declared = manifest.RelativeExecutable.Replace('\\', '/');
            string expected = $"versions/{manifest.Version}/{ExecutableName}";
            if (!string.Equals(declared, expected, StringComparison.OrdinalIgnoreCase)) return null;

            string executable = Path.Combine(VersionDirectory(manifest.Version), ExecutableName);
            return File.Exists(executable) ? executable : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void InstallReadyVersion(string ready, string version)
    {
        string versions = Path.Combine(_providerDirectory, "versions");
        string destination = VersionDirectory(version);
        Directory.CreateDirectory(versions);
        if (Directory.Exists(destination))
        {
            if (!File.Exists(Path.Combine(destination, ExecutableName)))
                throw new IOException($"Provider version directory is incomplete: {destination}");
            return;
        }
        Directory.Move(ready, destination);
    }

    private void PublishReadyVersion(string ready, string version, CancellationToken ct)
    {
        _publication.Run(() =>
        {
            InstallReadyVersion(ready, version);
            ActivateProtected(version);
        }, ct);
    }

    private string NewStagingDirectory()
    {
        string root = Path.Combine(_providerDirectory, "staging");
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N"));
    }

    private static void CopyDirectory(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void ValidateVersion(string version)
    {
        if (!IsValidVersion(version))
            throw new ArgumentException("A provider version must be one directory segment.", nameof(version));
    }

    private static bool IsValidVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version)
        && version is not "." and not ".."
        && version.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !version.Contains(Path.DirectorySeparatorChar)
        && !version.Contains(Path.AltDirectorySeparatorChar);

    private sealed record BackendActivation(string Version, string RelativeExecutable);
}
