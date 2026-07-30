using System.Security.Cryptography;
using System.Text.Json;

namespace Deckle.Autocorrect.Probe;

internal sealed record QwenArtifactIdentity(string Path, long Bytes, string Sha256);

internal sealed record QwenArtifactManifest(
    int SchemaVersion,
    string ModelDirectory,
    bool CompleteModelDirectory,
    IReadOnlyList<QwenArtifactIdentity> Files);

internal sealed record QwenAdapterGraphVerification(
    int SchemaVersion,
    bool Valid,
    QwenArtifactIdentity Graph,
    int LoRaMatMulNodeCount,
    int LoRaInitializerCount,
    int ExpectedLoRaCount,
    int NonExcludedBaseMatMulCount,
    int QuantizedNonExcludedBaseMatMulCount,
    int DuplicateNodeNameCount,
    int DuplicateInitializerNameCount,
    bool ExactMatMulNodeSet,
    bool ExactLoRaInitializerSet,
    bool LoRaInitializersPositiveZero,
    bool ExactInt4Attributes,
    bool ExactInt4WeightWiring,
    bool ExactLoRaWiring,
    bool ExactLoRaConsumerReplacement,
    IReadOnlyList<string> Errors);

internal sealed record QwenNegativeArtifactRecord(
    string Id,
    string MutationKind,
    string Path,
    bool Exists,
    QwenArtifactIdentity? Artifact);

internal sealed record QwenNegativeArtifactVerification(
    int SchemaVersion,
    string ExperimentId,
    string Phase,
    bool Valid,
    QwenArtifactIdentity ControlNpz,
    QwenArtifactIdentity ControlAdapter,
    IReadOnlyList<QwenNegativeArtifactRecord> Negatives);

internal sealed record QwenAdapterArtifactVerdict(bool Accepted, string Code)
{
    public const string AcceptedCode = "accepted";
    public const string IdentityMismatchCode = "identity_mismatch";
    public const string IncompleteModelDirectoryCode = "incomplete_model_directory";
    public const string InvalidGraphVerificationCode = "invalid_graph_verification";
    public const string MissingConsumedArtifactCode = "missing_consumed_artifact";
}

internal static class QwenAdapterArtifactGate
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static QwenAdapterArtifactVerdict Evaluate(QwenAdapterCompatibilityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!MatchesIdentity(plan.ArtifactManifest))
            return Reject(QwenAdapterArtifactVerdict.IdentityMismatchCode);

        QwenArtifactManifest manifest;
        QwenAdapterGraphVerification graph;
        QwenNegativeArtifactVerification negativeVerification;
        try
        {
            manifest = Deserialize<QwenArtifactManifest>(plan.ArtifactManifest.Path);
            graph = Deserialize<QwenAdapterGraphVerification>(plan.GraphVerification.Path);
            negativeVerification = Deserialize<QwenNegativeArtifactVerification>(
                plan.NegativeVerification.Path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or NotSupportedException or InvalidDataException)
        {
            return Reject(QwenAdapterArtifactVerdict.IdentityMismatchCode);
        }

        if (manifest.SchemaVersion != 1
            || !manifest.CompleteModelDirectory
            || !SamePath(manifest.ModelDirectory, plan.ModelDirectory)
            || manifest.Files is null
            || manifest.Files.Count == 0
            || manifest.Files.Select(static file => Path.GetFullPath(file.Path))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
            return Reject(QwenAdapterArtifactVerdict.IncompleteModelDirectoryCode);

        foreach (QwenArtifactIdentity file in manifest.Files)
            if (!MatchesIdentity(file))
                return Reject(QwenAdapterArtifactVerdict.IdentityMismatchCode);

        string[] actualModelFiles = Directory.GetFiles(
                plan.ModelDirectory,
                "*",
                SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] declaredModelFiles = manifest.Files
            .Select(static file => Path.GetFullPath(file.Path))
            .Where(path => IsUnderDirectory(path, plan.ModelDirectory))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!actualModelFiles.SequenceEqual(declaredModelFiles, StringComparer.OrdinalIgnoreCase))
            return Reject(QwenAdapterArtifactVerdict.IncompleteModelDirectoryCode);

        string[] consumed = plan.Adapters
            .SelectMany(static adapter => new[] { adapter.Path, adapter.ManifestPath })
            .Concat(plan.RuntimeNegatives.Skip(1).Select(static negative => negative.Path))
            .Append(plan.GraphVerification.Path)
            .Append(plan.NegativeVerification.Path)
            .Select(Path.GetFullPath)
            .ToArray();
        HashSet<string> declared = manifest.Files
            .Select(static file => Path.GetFullPath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (consumed.Any(path => !declared.Contains(path)))
            return Reject(QwenAdapterArtifactVerdict.MissingConsumedArtifactCode);

        if (!MatchesIdentity(plan.NegativeVerification)
            || negativeVerification.SchemaVersion != 1
            || !string.Equals(
                negativeVerification.ExperimentId,
                plan.ExperimentId,
                StringComparison.Ordinal)
            || !string.Equals(
                negativeVerification.Phase,
                plan.Phase,
                StringComparison.Ordinal)
            || !negativeVerification.Valid
            || negativeVerification.Negatives is null
            || negativeVerification.Negatives.Count != plan.RuntimeNegatives.Count
            || !MatchesIdentity(negativeVerification.ControlNpz)
            || !MatchesIdentity(negativeVerification.ControlAdapter)
            || !SamePath(
                negativeVerification.ControlAdapter.Path,
                plan.Adapters[0].Path)
            || !declared.Contains(Path.GetFullPath(
                negativeVerification.ControlNpz.Path))
            || !declared.Contains(Path.GetFullPath(
                negativeVerification.ControlAdapter.Path))
            || !ExactNegativeSet(
                plan.RuntimeNegatives,
                negativeVerification.Negatives,
                declared))
            return Reject(QwenAdapterArtifactVerdict.IdentityMismatchCode);

        if (!TryResolveConfiguredGraph(plan.ModelDirectory, out string configuredGraph)
            || !SamePath(graph.Graph.Path, configuredGraph)
            || !declared.Contains(Path.GetFullPath(configuredGraph))
            || !MatchesIdentity(plan.GraphVerification)
            || graph.SchemaVersion != 2
            || !graph.Valid
            || graph.Errors is null
            || graph.Errors.Count != 0
            || graph.ExpectedLoRaCount != 112
            || graph.LoRaMatMulNodeCount != 112
            || graph.LoRaInitializerCount != 112
            || graph.NonExcludedBaseMatMulCount <= 0
            || graph.QuantizedNonExcludedBaseMatMulCount
                != graph.NonExcludedBaseMatMulCount
            || graph.DuplicateNodeNameCount != 0
            || graph.DuplicateInitializerNameCount != 0
            || !graph.ExactMatMulNodeSet
            || !graph.ExactLoRaInitializerSet
            || !graph.LoRaInitializersPositiveZero
            || !graph.ExactInt4Attributes
            || !graph.ExactInt4WeightWiring
            || !graph.ExactLoRaWiring
            || !graph.ExactLoRaConsumerReplacement
            || !MatchesIdentity(graph.Graph)
            || !string.Equals(
                graph.Graph.Sha256,
                plan.Base.GraphSha256,
                StringComparison.Ordinal))
            return Reject(QwenAdapterArtifactVerdict.InvalidGraphVerificationCode);

        return new QwenAdapterArtifactVerdict(
            true,
            QwenAdapterArtifactVerdict.AcceptedCode);
    }

    private static bool ExactNegativeSet(
        IReadOnlyList<QwenAdapterRuntimeNegativePlan> plans,
        IReadOnlyList<QwenNegativeArtifactRecord> records,
        IReadOnlySet<string> declared)
    {
        for (int index = 0; index < plans.Count; index++)
        {
            QwenAdapterRuntimeNegativePlan plan = plans[index];
            QwenNegativeArtifactRecord record = records[index];
            bool shouldExist = !string.Equals(
                plan.Id,
                "missing-file",
                StringComparison.Ordinal);
            if (!string.Equals(plan.Id, record.Id, StringComparison.Ordinal)
                || !string.Equals(
                    record.MutationKind,
                    plan.Id.Replace('-', '_'),
                    StringComparison.Ordinal)
                || !SamePath(plan.Path, record.Path)
                || record.Exists != shouldExist)
                return false;

            if (!shouldExist)
            {
                if (record.Artifact is not null || File.Exists(record.Path))
                    return false;
                continue;
            }

            if (record.Artifact is null
                || !SamePath(record.Path, record.Artifact.Path)
                || !MatchesIdentity(record.Artifact)
                || !declared.Contains(Path.GetFullPath(record.Path)))
                return false;
        }

        return true;
    }

    internal static bool TryResolveConfiguredGraph(
        string modelDirectory,
        out string graphPath)
    {
        graphPath = string.Empty;
        try
        {
            string modelRoot = Path.GetFullPath(modelDirectory);
            string configPath = Path.Combine(modelRoot, "genai_config.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("model", out JsonElement model)
                || !model.TryGetProperty("decoder", out JsonElement decoder)
                || !decoder.TryGetProperty("filename", out JsonElement filenameElement)
                || filenameElement.ValueKind != JsonValueKind.String)
                return false;

            string? filename = filenameElement.GetString();
            if (string.IsNullOrWhiteSpace(filename) || Path.IsPathFullyQualified(filename))
                return false;

            string candidate = Path.GetFullPath(Path.Combine(modelRoot, filename));
            if (!IsUnderDirectory(candidate, modelRoot) || !File.Exists(candidate))
                return false;

            graphPath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException
                or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool MatchesIdentity(QwenArtifactIdentity identity)
    {
        if (identity is null || !File.Exists(identity.Path))
            return false;

        var file = new FileInfo(identity.Path);
        if (file.Length != identity.Bytes)
            return false;

        using FileStream stream = File.OpenRead(identity.Path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        return string.Equals(actual, identity.Sha256, StringComparison.Ordinal);
    }

    private static T Deserialize<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Empty JSON artifact: {path}");

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderDirectory(string path, string directory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static QwenAdapterArtifactVerdict Reject(string code) => new(false, code);
}
