namespace Deckle.Autocorrect.Probe;

internal sealed record QwenAdapterManifest(
    string BaseRepository,
    string BaseRevision,
    string GraphSha256,
    IReadOnlyList<string> TargetModules,
    int Rank,
    string DType,
    IReadOnlyList<QwenAdapterTensorContract> Tensors);

internal sealed record QwenAdapterManifestVerdict(bool Accepted, string Code)
{
    public const string AcceptedCode = "accepted";
    public const string InvalidManifestCode = "invalid_manifest";
    public const string BaseRepositoryMismatchCode = "base_repository_mismatch";
    public const string BaseRevisionMismatchCode = "base_revision_mismatch";
    public const string GraphHashMismatchCode = "graph_hash_mismatch";
    public const string TargetModulesMismatchCode = "target_modules_mismatch";
    public const string RankMismatchCode = "rank_mismatch";
    public const string DTypeMismatchCode = "dtype_mismatch";
    public const string TensorCountMismatchCode = "tensor_count_mismatch";
    public const string TensorNameMismatchCode = "tensor_name_mismatch";
    public const string TensorShapeMismatchCode = "tensor_shape_mismatch";
    public const string TensorDTypeMismatchCode = "tensor_dtype_mismatch";
}

internal static class QwenAdapterManifestGate
{
    public static QwenAdapterManifestVerdict Evaluate(
        QwenAdapterManifest expected,
        QwenAdapterManifest actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        if (!IsValid(expected) || !IsValid(actual))
            return Reject(QwenAdapterManifestVerdict.InvalidManifestCode);
        if (!string.Equals(
                expected.BaseRepository,
                actual.BaseRepository,
                StringComparison.Ordinal))
            return Reject(QwenAdapterManifestVerdict.BaseRepositoryMismatchCode);
        if (!string.Equals(
                expected.BaseRevision,
                actual.BaseRevision,
                StringComparison.Ordinal))
            return Reject(QwenAdapterManifestVerdict.BaseRevisionMismatchCode);
        if (!string.Equals(
                expected.GraphSha256,
                actual.GraphSha256,
                StringComparison.Ordinal))
            return Reject(QwenAdapterManifestVerdict.GraphHashMismatchCode);
        if (!expected.TargetModules.SequenceEqual(
                actual.TargetModules,
                StringComparer.Ordinal))
            return Reject(QwenAdapterManifestVerdict.TargetModulesMismatchCode);
        if (expected.Rank != actual.Rank)
            return Reject(QwenAdapterManifestVerdict.RankMismatchCode);
        if (!string.Equals(expected.DType, actual.DType, StringComparison.Ordinal))
            return Reject(QwenAdapterManifestVerdict.DTypeMismatchCode);
        if (expected.Tensors.Count != actual.Tensors.Count)
            return Reject(QwenAdapterManifestVerdict.TensorCountMismatchCode);

        for (int i = 0; i < expected.Tensors.Count; i++)
        {
            QwenAdapterTensorContract expectedTensor = expected.Tensors[i];
            QwenAdapterTensorContract actualTensor = actual.Tensors[i];
            if (!string.Equals(expectedTensor.Name, actualTensor.Name, StringComparison.Ordinal))
                return Reject(QwenAdapterManifestVerdict.TensorNameMismatchCode);
            if (!expectedTensor.Shape.SequenceEqual(actualTensor.Shape))
                return Reject(QwenAdapterManifestVerdict.TensorShapeMismatchCode);
            if (!string.Equals(expectedTensor.DType, actualTensor.DType, StringComparison.Ordinal))
                return Reject(QwenAdapterManifestVerdict.TensorDTypeMismatchCode);
        }

        return new QwenAdapterManifestVerdict(true, QwenAdapterManifestVerdict.AcceptedCode);
    }

    private static bool IsValid(QwenAdapterManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.BaseRepository)
            || !IsLowerHex(manifest.BaseRevision, 40)
            || !IsLowerHex(manifest.GraphSha256, 64)
            || manifest.TargetModules.Count == 0
            || manifest.TargetModules.Any(string.IsNullOrWhiteSpace)
            || manifest.TargetModules.Distinct(StringComparer.Ordinal).Count()
                != manifest.TargetModules.Count
            || manifest.Rank <= 0
            || string.IsNullOrWhiteSpace(manifest.DType)
            || manifest.Tensors.Count == 0
            || manifest.Tensors.Select(static tensor => tensor.Name)
                    .Distinct(StringComparer.Ordinal).Count()
                != manifest.Tensors.Count)
            return false;

        return manifest.Tensors.All(tensor =>
            !string.IsNullOrWhiteSpace(tensor.Name)
            && tensor.Shape.Count == 2
            && tensor.Shape.All(static dimension => dimension > 0)
            && !string.IsNullOrWhiteSpace(tensor.DType));
    }

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length
        && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static QwenAdapterManifestVerdict Reject(string code) => new(false, code);
}

internal interface IQwenAdapterLoader
{
    void LoadAdapter(string path, string name);
}

internal enum QwenAdapterLoadStage
{
    ProbePolicy,
    Runtime,
}

internal sealed record QwenAdapterLoadOutcome(
    bool Loaded,
    QwenAdapterLoadStage Stage,
    string Code,
    string? ExceptionType)
{
    public const string LoadedCode = "loaded";
    public const string RuntimeLoadFailedCode = "runtime_load_failed";
}

internal static class QwenAdapterPolicyLoader
{
    public static QwenAdapterLoadOutcome TryLoad(
        QwenAdapterManifest expected,
        QwenAdapterManifest actual,
        string path,
        string name,
        IQwenAdapterLoader loader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(loader);

        QwenAdapterManifestVerdict verdict =
            QwenAdapterManifestGate.Evaluate(expected, actual);
        if (!verdict.Accepted)
        {
            return new QwenAdapterLoadOutcome(
                false,
                QwenAdapterLoadStage.ProbePolicy,
                verdict.Code,
                null);
        }

        try
        {
            loader.LoadAdapter(path, name);
            return new QwenAdapterLoadOutcome(
                true,
                QwenAdapterLoadStage.Runtime,
                QwenAdapterLoadOutcome.LoadedCode,
                null);
        }
        catch (Exception exception)
        {
            return new QwenAdapterLoadOutcome(
                false,
                QwenAdapterLoadStage.Runtime,
                QwenAdapterLoadOutcome.RuntimeLoadFailedCode,
                exception.GetType().Name);
        }
    }
}
