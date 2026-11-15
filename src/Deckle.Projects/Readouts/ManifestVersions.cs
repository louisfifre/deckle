using System.Text.Json;
using System.Xml;

namespace Deckle.Projects;

// What the manifests found in a project say its version is (spec, Project
// page, Readouts): Cargo.toml, package.json, *.csproj and
// Directory.Build.props, pyproject.toml, gradle.properties (mod_version) and
// build.gradle.
//
// Which files: inside a Git repository the candidates are the ones git lists
// (tracked and untracked but not ignored), so node_modules and target never
// appear and the listing is fast. In a folder that is not a repository, a
// bounded walk stands in, skipping the folders that only hold build output.
//
// A file with no version, a version made of variables, one over a mebibyte or
// one that cannot be parsed gives no source: a manifest never fails the
// reading, it says one Verbose event naming its format.
internal static class ManifestVersions
{
    // A repository with many package.json files gives more lines than are
    // useful (the spec says so itself): the flyout lists this many and says
    // how many more there are.
    public const int MaxSources = 20;

    // How many candidate files are opened at most, so a monorepo with
    // thousands of manifests costs a bounded read.
    public const int MaxCandidates = 400;

    public const long MaxFileBytes = 1024 * 1024;

    private const int WalkDepth = 4;

    private static readonly string[] SkippedFolders =
        ["node_modules", ".git", "bin", "obj", "target", "build", "dist", ".venv"];

    public static bool IsManifestName(string fileName) =>
        fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("gradle.properties", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("build.gradle", StringComparison.OrdinalIgnoreCase);

    // The manifest files in a list of relative paths (git's, with "/" between
    // segments), the shallowest first and alphabetical within a depth, so the
    // root's manifest leads and a given project always lists in one order.
    public static IReadOnlyList<string> Candidates(IEnumerable<string> relativePaths) =>
        [.. relativePaths
            .Select(p => p.Replace('\\', '/'))
            .Where(p => IsManifestName(p[(p.LastIndexOf('/') + 1)..]))
            .Where(p => !p.Split('/').SkipLast(1).Any(IsSkippedFolder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];

    // The candidates of a folder that is not a repository: a walk of depth 4
    // that skips the folders holding build output.
    public static IReadOnlyList<string> Walk(string root)
    {
        var found = new List<string>();
        Visit(root, "", 0, found);
        return Candidates(found);
    }

    private static void Visit(string folder, string relative, int depth, List<string> found)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (IsManifestName(name)) found.Add(relative.Length == 0 ? name : relative + "/" + name);
            }
            if (depth >= WalkDepth) return;

            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                string name = Path.GetFileName(child);
                if (IsSkippedFolder(name)) continue;
                if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) continue;
                Visit(child, relative.Length == 0 ? name : relative + "/" + name, depth + 1, found);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed has no manifest to show.
        }
    }

    private static bool IsSkippedFolder(string name) =>
        SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase);

    // Reads the candidates in order and keeps the ones that declare a version,
    // up to MaxSources; the others that do are counted, not listed.
    public static ManifestScan Read(string root, IReadOnlyList<string> candidates)
    {
        var sources = new List<VersionSource>();
        int more = 0;
        foreach (string relative in candidates.Take(MaxCandidates))
        {
            string? version = ReadFile(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), relative);
            if (version is null) continue;
            if (sources.Count < MaxSources) sources.Add(new VersionSource(relative, TrimV(version)));
            else more++;
        }
        return new ManifestScan(sources, more);
    }

    // The version one manifest declares, or null. Public to the engine so the
    // parsers are reached by file name the way the reading reaches them.
    public static string? Parse(string fileName, string text)
    {
        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase))
            return MsBuildManifest.Version(text);
        if (fileName.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)) return CargoManifest.Version(text);
        if (fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)) return PackageJsonManifest.Version(text);
        if (fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)) return PyProjectManifest.Version(text);
        if (fileName.Equals("gradle.properties", StringComparison.OrdinalIgnoreCase)) return GradleManifest.PropertiesVersion(text);
        if (fileName.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)) return GradleManifest.BuildScriptVersion(text);
        return null;
    }

    private static string? ReadFile(string path, string relative)
    {
        string name = relative[(relative.LastIndexOf('/') + 1)..];
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            return Parse(name, File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or XmlException or IOException or UnauthorizedAccessException)
        {
            DeckleProjectsSource.Log.ManifestUnreadable(FormatName(name));
            return null;
        }
    }

    private static string FormatName(string fileName) =>
        fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
            ? "msbuild"
            : fileName.ToLowerInvariant();

    private static string TrimV(string version) => GitTags.WithoutV(version);
}

internal sealed record ManifestScan(IReadOnlyList<VersionSource> Sources, int More);
