using System.Text.Json;

namespace Deckle.Autocorrect.Probe;

internal sealed record QwenAdapterCompatibilityPlan(
    int SchemaVersion,
    string ExperimentId,
    string Phase,
    int FreshProcessOrdinal,
    string Provider,
    string ModelDirectory,
    string Prompt,
    string OutputPath,
    string CrossModelOutputPath,
    QwenArtifactIdentity ArtifactManifest,
    QwenArtifactIdentity GraphVerification,
    QwenArtifactIdentity NegativeVerification,
    IReadOnlyList<long> ExpectedLogitsShape,
    QwenAdapterCandidateOraclePlan CandidateOracle,
    QwenAdapterBaseContract Base,
    IReadOnlyList<QwenAdapterArtifactPlan> Adapters,
    IReadOnlyList<QwenAdapterRuntimeNegativePlan> RuntimeNegatives);

internal sealed record QwenAdapterBaseContract(
    string Repository,
    string Revision,
    string GraphSha256,
    IReadOnlyList<string> TargetModules,
    int LayerCount,
    int InputSize,
    int QueryOutputSize,
    int ValueOutputSize,
    int Rank,
    string DType)
{
    public QwenAdapterManifest ToManifest()
    {
        QwenAdapterGraphContract graph = QwenAdapterGraphContract.Create(
            LayerCount,
            InputSize,
            QueryOutputSize,
            ValueOutputSize,
            Rank);

        return new QwenAdapterManifest(
            Repository,
            Revision,
            GraphSha256,
            TargetModules,
            Rank,
            DType,
            graph.Tensors);
    }
}

internal sealed record QwenAdapterArtifactPlan(
    string Name,
    string Path,
    string ManifestPath);

internal sealed record QwenAdapterCandidateOraclePlan(
    IReadOnlyList<int> PromptTokenIds,
    IReadOnlyList<QwenAdapterCandidateCompletionPlan> Candidates);

internal sealed record QwenAdapterCandidateCompletionPlan(
    string Id,
    string Text,
    IReadOnlyList<int> CompletionTokenIds,
    int ScoreStartInclusive,
    int ScoreEndExclusive);

internal sealed record QwenAdapterRuntimeNegativePlan(
    string Id,
    string Path,
    string Name,
    string ExpectedExceptionType);

internal sealed record QwenAdapterPlanVerdict(bool Accepted, string Code)
{
    public const string AcceptedCode = "accepted";
    public const string InvalidJsonCode = "invalid_json";
    public const string UnsupportedProtocolCode = "unsupported_protocol";
    public const string InvalidPathCode = "invalid_path";
    public const string InvalidBaseContractCode = "invalid_base_contract";
    public const string InvalidAdapterSetCode = "invalid_adapter_set";
    public const string InvalidNegativeSetCode = "invalid_negative_set";
}

internal static class QwenAdapterCompatibilityPlanReader
{
    private const string FrozenExperimentId = "ACX-0023";
    private const string FrozenPhase = "A";
    private const string FrozenProvider = "cpu";
    private const string FrozenRepository = "Qwen/Qwen3-0.6B";
    private const string FrozenRevision = "c1899de289a04d12100db370d81485cdf75e47ca";
    private static readonly string[] FrozenRuntimeNegativeIds =
    [
        "missing-file",
        "truncated-file",
        "wrong-model-version",
        "wrong-target-name",
        "wrong-rank-shape",
        "wrong-dtype",
        "missing-tensor",
        "extra-tensor",
    ];
    private static readonly string[] FrozenCandidateTexts =
    [
        "je suis la",
        "je suis là",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static (QwenAdapterCompatibilityPlan? Plan, QwenAdapterPlanVerdict Verdict)
        TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            QwenAdapterCompatibilityPlan? plan = JsonSerializer.Deserialize<
                QwenAdapterCompatibilityPlan>(File.ReadAllText(path), JsonOptions);
            if (plan is null)
                return Reject(QwenAdapterPlanVerdict.InvalidJsonCode);

            QwenAdapterPlanVerdict verdict = Evaluate(plan);
            return verdict.Accepted ? (plan, verdict) : (null, verdict);
        }
        catch (JsonException)
        {
            return Reject(QwenAdapterPlanVerdict.InvalidJsonCode);
        }
        catch (NotSupportedException)
        {
            return Reject(QwenAdapterPlanVerdict.InvalidJsonCode);
        }
        catch (IOException)
        {
            return Reject(QwenAdapterPlanVerdict.InvalidPathCode);
        }
        catch (UnauthorizedAccessException)
        {
            return Reject(QwenAdapterPlanVerdict.InvalidPathCode);
        }
    }

    public static QwenAdapterPlanVerdict Evaluate(QwenAdapterCompatibilityPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.SchemaVersion != 1
            || !string.Equals(plan.ExperimentId, FrozenExperimentId, StringComparison.Ordinal)
            || !string.Equals(plan.Phase, FrozenPhase, StringComparison.Ordinal)
            || plan.FreshProcessOrdinal is < 1 or > 5
            || !string.Equals(plan.Provider, FrozenProvider, StringComparison.Ordinal))
            return RejectOnly(QwenAdapterPlanVerdict.UnsupportedProtocolCode);

        if (!IsAbsolute(plan.ModelDirectory)
            || !IsAbsolute(plan.OutputPath)
            || !IsAbsolute(plan.CrossModelOutputPath)
            || string.Equals(
                plan.OutputPath,
                plan.CrossModelOutputPath,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(plan.Prompt))
            return RejectOnly(QwenAdapterPlanVerdict.InvalidPathCode);

        if (!IsValidIdentity(plan.ArtifactManifest)
            || !IsValidIdentity(plan.GraphVerification)
            || !IsValidIdentity(plan.NegativeVerification)
            || plan.ExpectedLogitsShape is null
            || plan.ExpectedLogitsShape.Count != 3
            || plan.ExpectedLogitsShape.Any(static dimension => dimension <= 0)
            || plan.ExpectedLogitsShape[0] != 1
            || plan.ExpectedLogitsShape[^1] != 151936
            || !IsValidCandidateOracle(plan.CandidateOracle, plan.ExpectedLogitsShape))
            return RejectOnly(QwenAdapterPlanVerdict.InvalidBaseContractCode);

        if (!IsFrozenBase(plan.Base))
            return RejectOnly(QwenAdapterPlanVerdict.InvalidBaseContractCode);

        if (!IsFrozenAdapterSet(plan.Adapters))
            return RejectOnly(QwenAdapterPlanVerdict.InvalidAdapterSetCode);

        if (!IsValidNegativeSet(plan.RuntimeNegatives))
            return RejectOnly(QwenAdapterPlanVerdict.InvalidNegativeSetCode);

        return new QwenAdapterPlanVerdict(true, QwenAdapterPlanVerdict.AcceptedCode);
    }

    public static QwenAdapterManifest ReadManifest(string path)
    {
        QwenAdapterManifest? manifest = JsonSerializer.Deserialize<QwenAdapterManifest>(
            File.ReadAllText(path),
            JsonOptions);
        return manifest ?? throw new InvalidDataException("Adapter manifest is empty.");
    }

    public static string SerializeReport(QwenAdapterCompatibilityReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    private static bool IsFrozenBase(QwenAdapterBaseContract contract) =>
        contract is not null
        && string.Equals(contract.Repository, FrozenRepository, StringComparison.Ordinal)
        && string.Equals(contract.Revision, FrozenRevision, StringComparison.Ordinal)
        && IsLowerHex(contract.GraphSha256, 64)
        && contract.TargetModules is not null
        && contract.TargetModules.SequenceEqual(["q_proj", "v_proj"], StringComparer.Ordinal)
        && contract.LayerCount == 28
        && contract.InputSize == 1024
        && contract.QueryOutputSize == 2048
        && contract.ValueOutputSize == 1024
        && contract.Rank == 8
        && string.Equals(contract.DType, "float16", StringComparison.Ordinal);

    private static bool IsFrozenAdapterSet(IReadOnlyList<QwenAdapterArtifactPlan> adapters)
    {
        if (adapters is null || adapters.Count != 2)
            return false;
        if (adapters.Any(static adapter => adapter is null))
            return false;

        string[] names = adapters.Select(static adapter => adapter.Name).ToArray();
        if (!names.SequenceEqual(["control-zero", "sentinel-seeded"], StringComparer.Ordinal))
            return false;

        return adapters.All(static adapter =>
            IsAbsolute(adapter.Path)
            && IsAbsolute(adapter.ManifestPath));
    }

    private static bool IsValidCandidateOracle(
        QwenAdapterCandidateOraclePlan oracle,
        IReadOnlyList<long> expectedLogitsShape)
    {
        if (oracle is null
            || oracle.PromptTokenIds is null
            || oracle.PromptTokenIds.Count == 0
            || oracle.PromptTokenIds[0] != 151643
            || oracle.PromptTokenIds.Count != expectedLogitsShape[1]
            || oracle.Candidates is null
            || oracle.Candidates.Count != 2
            || oracle.Candidates.Any(static candidate => candidate is null)
            || !oracle.Candidates.Select(static candidate => candidate.Id)
                .SequenceEqual(["literal", "corrected"], StringComparer.Ordinal)
            || !oracle.Candidates.Select(static candidate => candidate.Text)
                .SequenceEqual(FrozenCandidateTexts, StringComparer.Ordinal))
            return false;

        int vocabularySize = checked((int)expectedLogitsShape[^1]);
        if (oracle.PromptTokenIds.Any(token => token < 0 || token >= vocabularySize))
            return false;

        foreach (QwenAdapterCandidateCompletionPlan candidate in oracle.Candidates)
        {
            if (candidate.CompletionTokenIds is null
                || candidate.CompletionTokenIds.Count == 0
                || candidate.CompletionTokenIds.Any(token =>
                    token < 0 || token >= vocabularySize)
                || candidate.ScoreStartInclusive < 0
                || candidate.ScoreEndExclusive <= candidate.ScoreStartInclusive
                || candidate.ScoreEndExclusive > candidate.CompletionTokenIds.Count)
                return false;
        }

        int commonPrefix = CommonPrefixLength(oracle.Candidates);
        bool emptyScoredTail = oracle.Candidates.Any(candidate =>
            candidate.CompletionTokenIds.Count - commonPrefix <= 0);
        int expectedStart = emptyScoredTail ? 0 : commonPrefix;
        if (oracle.Candidates.Any(candidate =>
                candidate.ScoreStartInclusive != expectedStart
                || candidate.ScoreEndExclusive != candidate.CompletionTokenIds.Count))
            return false;

        return !oracle.Candidates[0].CompletionTokenIds.SequenceEqual(
            oracle.Candidates[1].CompletionTokenIds);
    }

    private static bool IsValidNegativeSet(
        IReadOnlyList<QwenAdapterRuntimeNegativePlan> negatives)
    {
        if (negatives is null || negatives.Count != FrozenRuntimeNegativeIds.Length)
            return false;
        if (negatives.Any(static negative => negative is null))
            return false;

        return negatives.Select(static negative => negative.Id)
                .SequenceEqual(FrozenRuntimeNegativeIds, StringComparer.Ordinal)
            && negatives.All(static negative =>
                !string.IsNullOrWhiteSpace(negative.Id)
                && string.Equals(
                    negative.Name,
                    $"negative-{negative.Id}",
                    StringComparison.Ordinal)
                && string.Equals(
                    negative.ExpectedExceptionType,
                    "OnnxRuntimeGenAIException",
                    StringComparison.Ordinal)
                && IsAbsolute(negative.Path))
            && negatives.Select(static negative => negative.Name)
                .Distinct(StringComparer.Ordinal).Count() == negatives.Count;
    }

    private static bool IsAbsolute(string value) =>
        !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value);

    private static bool IsValidIdentity(QwenArtifactIdentity identity) =>
        identity is not null
        && IsAbsolute(identity.Path)
        && identity.Bytes > 0
        && IsLowerHex(identity.Sha256, 64);

    private static int CommonPrefixLength(
        IReadOnlyList<QwenAdapterCandidateCompletionPlan> candidates)
    {
        int minimum = candidates.Min(static candidate =>
            candidate.CompletionTokenIds.Count);
        int prefix = 0;
        while (prefix < minimum
            && candidates.All(candidate =>
                candidate.CompletionTokenIds[prefix]
                    == candidates[0].CompletionTokenIds[prefix]))
            prefix++;
        return prefix;
    }

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static (QwenAdapterCompatibilityPlan?, QwenAdapterPlanVerdict) Reject(
        string code) =>
        (null, RejectOnly(code));

    private static QwenAdapterPlanVerdict RejectOnly(string code) => new(false, code);
}
