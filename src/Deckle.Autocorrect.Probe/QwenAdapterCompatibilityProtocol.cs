using System.Diagnostics;
using Deckle.Autocorrect.Onnx;

namespace Deckle.Autocorrect.Probe;

internal interface IQwenAdapterProbeRuntimeFactory
{
    IQwenAdapterProbeRuntime Create(QwenAdapterCompatibilityPlan plan);
}

internal interface IQwenAdapterProbeRuntime : IQwenAdapterLoader, IDisposable
{
    int ModelInstanceCount { get; }
    double ModelLoadMilliseconds { get; }
    IReadOnlyList<string> CleanupFailures { get; }
    IReadOnlyList<int> Encode(string text);
    IQwenAdapterProbeRequest CreateRequest(IReadOnlyList<int> tokens);
    void UnloadAdapter(string name);
}

internal interface IQwenAdapterProbeRequest : IDisposable
{
    double GeneratorCreateMilliseconds { get; }
    double SetActiveAdapter(string name);
    QwenAdapterRuntimeObservation Execute(
        string state,
        int ordinal,
        bool retainComparisonValues = false);
    QwenForcedCandidateScore ScoreCandidate(
        string id,
        int promptTokenCount,
        IReadOnlyList<int> completionTokenIds,
        int scoreStartInclusive,
        int scoreEndExclusive);
}

internal sealed record QwenAdapterRuntimeObservation(
    string State,
    int Ordinal,
    string Sha256,
    IReadOnlyList<long> Shape,
    string DType,
    bool Finite,
    double GeneratorCreateMilliseconds,
    double ActivationMilliseconds,
    double FirstForwardMilliseconds,
    long ElementCount,
    Half[]? ComparisonValues);

internal sealed record QwenForcedCandidateScore(
    string Id,
    double Score,
    double LogProbability,
    int ScoredTokenCount,
    bool Finite,
    IReadOnlyList<long> LogitsShape);

internal sealed record QwenCandidateOracleObservation(
    string State,
    int Ordinal,
    IReadOnlyList<QwenForcedCandidateScore> Candidates,
    int WinnerIndex);

internal sealed record QwenAdapterObservationReport(
    string State,
    int Ordinal,
    string Sha256,
    IReadOnlyList<long> Shape,
    string DType,
    bool Finite,
    double GeneratorCreateMilliseconds,
    double ActivationMilliseconds,
    double FirstForwardMilliseconds,
    IReadOnlyList<double> CandidateScores,
    int CandidateWinnerIndex);

internal sealed record QwenAdapterNegativeOutcome(
    string Id,
    string ExpectedExceptionType,
    bool ExpectedFailureObserved,
    string? ExceptionType,
    bool RecoveryPassed);

internal sealed record QwenAdapterTimingSample(
    double WallMilliseconds,
    double ProcessCpuMilliseconds,
    long CurrentThreadAllocatedBytes);

internal sealed record QwenAdapterTimingSeries(
    string Name,
    IReadOnlyList<QwenAdapterTimingSample> RawSamples,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaxMilliseconds);

internal sealed record QwenAdapterCompatibilityReport(
    string ExperimentId,
    string Phase,
    int FreshProcessOrdinal,
    string Provider,
    bool Valid,
    string Verdict,
    int ModelInstanceCount,
    IReadOnlyList<QwenAdapterLoadOutcome> Loads,
    IReadOnlyList<QwenAdapterNegativeOutcome> Negatives,
    IReadOnlyList<QwenAdapterObservationReport> Observations,
    IReadOnlyList<QwenCandidateOracleObservation> CandidateOracleObservations,
    IReadOnlyList<QwenAdapterTimingSeries> Timings,
    IReadOnlyList<QwenResourceTransition> Resources,
    IReadOnlyList<string> CleanupFailures,
    string? FatalExceptionType,
    string? FatalStage,
    double? BaseControlMaximumAbsoluteDelta,
    double? ControlRepeatabilityMaximumAbsoluteDelta,
    double? SentinelRepeatabilityMaximumAbsoluteDelta,
    double? ControlSentinelMinimumMaximumAbsoluteDelta,
    double? BaseControlCandidateScoreMaximumAbsoluteDelta,
    double? BaseControlCandidateLogProbabilityMaximumAbsoluteDelta,
    bool BaseControlCandidateWinnerStable,
    string ClaimBoundary);

internal sealed class QwenAdapterCompatibilityRunner
{
    private sealed record QwenInitialTensorReferences(
        QwenAdapterRuntimeObservation Base,
        QwenAdapterRuntimeObservation Control,
        QwenAdapterRuntimeObservation Sentinel,
        double? ControlSentinelMaximumDelta);

    internal const string ValidVerdict = "valid_phase_a";
    internal const string InvalidManifestVerdict = "invalid_manifest_gate";
    internal const string InvalidRuntimeVerdict = "invalid_runtime_contract";
    internal const string ClaimBoundary =
        "Exact ACX-0023 Phase-A CPU adapter lifecycle and numerical isolation only; "
        + "no DirectML, shared-native-session, memory-saving, task-quality, "
        + "autocorrection, field, end-to-end, UIA, or production claim.";

    private static readonly string?[] FrozenStateOrder =
    [
        null,
        "control-zero",
        "sentinel-seeded",
        "control-zero",
        "sentinel-seeded",
        "control-zero",
        null,
        "sentinel-seeded",
        "control-zero",
        "sentinel-seeded",
        "control-zero",
        "sentinel-seeded",
    ];

    private readonly IQwenAdapterProbeRuntimeFactory _runtimeFactory;

    public QwenAdapterCompatibilityRunner(IQwenAdapterProbeRuntimeFactory runtimeFactory)
    {
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public QwenAdapterCompatibilityReport Run(
        QwenAdapterCompatibilityPlan plan,
        IReadOnlyDictionary<string, QwenAdapterManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifests);

        QwenAdapterManifest expected = plan.Base.ToManifest();
        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
        {
            if (!manifests.TryGetValue(adapter.Name, out QwenAdapterManifest? actual)
                || !QwenAdapterManifestGate.Evaluate(expected, actual).Accepted)
            {
                return EmptyReport(
                    plan,
                    InvalidManifestVerdict,
                    Array.Empty<QwenAdapterLoadOutcome>());
            }
        }

        var resources = new List<QwenResourceTransition>();
        QwenMeasured<bool> samplerControl;
        try
        {
            samplerControl = QwenAdapterResourceSampler.Measure(
                "sampler_control_no_model",
                static () => true);
        }
        catch (Exception exception)
        {
            return FatalReport(plan, exception, "sampler_control");
        }
        resources.Add(samplerControl.Transition);
        QwenMeasured<IQwenAdapterProbeRuntime> modelLoad;
        try
        {
            modelLoad = QwenAdapterResourceSampler.Measure(
                "model_load",
                () => _runtimeFactory.Create(plan));
        }
        catch (QwenAdapterRuntimeCreationException exception)
        {
            return EmptyReport(
                plan,
                InvalidRuntimeVerdict,
                Array.Empty<QwenAdapterLoadOutcome>(),
                cleanupFailures: exception.CleanupFailures,
                fatalExceptionType: exception.OriginalExceptionType,
                fatalStage: "model_create",
                resources: resources.AsReadOnly());
        }
        catch (Exception exception)
        {
            return EmptyReport(
                plan,
                InvalidRuntimeVerdict,
                Array.Empty<QwenAdapterLoadOutcome>(),
                fatalExceptionType: exception.GetType().Name,
                fatalStage: "model_create",
                resources: resources.AsReadOnly());
        }
        resources.Add(modelLoad.Transition);
        using IQwenAdapterProbeRuntime runtime = modelLoad.Value;
        try
        {
        if (!CandidateOracleMatchesTokenizer(runtime, plan))
        {
            return EmptyReport(
                plan,
                InvalidRuntimeVerdict,
                Array.Empty<QwenAdapterLoadOutcome>(),
                runtime.ModelInstanceCount,
                runtime.CleanupFailures);
        }
        var loads = new List<QwenAdapterLoadOutcome>(plan.Adapters.Count);
        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
        {
            QwenMeasured<QwenAdapterLoadOutcome> measuredLoad =
                QwenAdapterResourceSampler.Measure(
                    $"adapter_load:{adapter.Name}",
                    () => QwenAdapterPolicyLoader.TryLoad(
                        expected,
                        manifests[adapter.Name],
                        adapter.Path,
                        adapter.Name,
                        runtime));
            resources.Add(measuredLoad.Transition);
            QwenAdapterLoadOutcome load = measuredLoad.Value;
            loads.Add(load);
            if (!load.Loaded)
                return EmptyReport(
                    plan,
                    InvalidRuntimeVerdict,
                    loads,
                    runtime.ModelInstanceCount,
                    runtime.CleanupFailures);
        }

        var observations = new List<QwenAdapterRuntimeObservation>();
        var candidateObservations = new List<QwenCandidateOracleObservation>();
        var negatives = new List<QwenAdapterNegativeOutcome>();

        QwenInitialTensorReferences tensorReferences = CaptureInitialTensorReferences(
            runtime,
            plan.CandidateOracle.PromptTokenIds);
        QwenAdapterRuntimeObservation baseReference = tensorReferences.Base;
        QwenAdapterRuntimeObservation controlReference = tensorReferences.Control;
        QwenAdapterRuntimeObservation sentinelReference = tensorReferences.Sentinel;
        candidateObservations.Add(ScoreCandidates(runtime, plan, null, -3));
        candidateObservations.Add(ScoreCandidates(runtime, plan, "control-zero", -2));
        candidateObservations.Add(ScoreCandidates(runtime, plan, "sentinel-seeded", -1));

        RunLoadNegative(
            runtime,
            plan.CandidateOracle.PromptTokenIds,
            "duplicate-name",
            plan.Adapters[0].Path,
            plan.Adapters[0].Name,
            "OnnxRuntimeGenAIException",
            controlReference,
            negatives);

        foreach (QwenAdapterRuntimeNegativePlan negative in plan.RuntimeNegatives)
        {
            RunLoadNegative(
                runtime,
                plan.CandidateOracle.PromptTokenIds,
                negative.Id,
                negative.Path,
                negative.Name,
                negative.ExpectedExceptionType,
                controlReference,
                negatives);
        }

        QwenMeasured<bool> alternating = QwenAdapterResourceSampler.Measure(
            "alternating_requests",
            () =>
            {
                for (int i = 0; i < FrozenStateOrder.Length; i++)
                {
                    observations.Add(Observe(
                        runtime,
                        plan.CandidateOracle.PromptTokenIds,
                        FrozenStateOrder[i],
                        i));
                    candidateObservations.Add(ScoreCandidates(
                        runtime,
                        plan,
                        FrozenStateOrder[i],
                        i));
                }
                return true;
            });
        resources.Add(alternating.Transition);

        QwenMeasured<bool> unloadLifecycle = QwenAdapterResourceSampler.Measure(
            "unload_lifecycle",
            () =>
            {
                RunUnloadLifecycle(
                    runtime,
                    plan.CandidateOracle.PromptTokenIds,
                    controlReference,
                    sentinelReference,
                    observations,
                    negatives);
                return true;
            });
        resources.Add(unloadLifecycle.Transition);

        IReadOnlyList<QwenAdapterTimingSeries> timings = RunTimingProtocol(
            runtime,
            plan,
            modelLoad.Transition,
            observations);

        bool outputsValid =
            IsValidCompactObservation(baseReference, plan.ExpectedLogitsShape)
            && IsValidCompactObservation(controlReference, plan.ExpectedLogitsShape)
            && IsValidCompactObservation(sentinelReference, plan.ExpectedLogitsShape)
            && observations.All(observation =>
                IsValidCompactObservation(observation, plan.ExpectedLogitsShape));
        bool candidateOutputsValid = candidateObservations.All(observation =>
            observation.Candidates.Count == plan.CandidateOracle.Candidates.Count
            && observation.Candidates.Select(static candidate => candidate.Id)
                .SequenceEqual(
                    plan.CandidateOracle.Candidates.Select(static candidate => candidate.Id),
                    StringComparer.Ordinal)
            && observation.Candidates.All(static candidate =>
                candidate.Finite
                && double.IsFinite(candidate.Score)
                && double.IsFinite(candidate.LogProbability))
            && observation.Candidates.Select(static candidate => candidate.ScoredTokenCount)
                .SequenceEqual(plan.CandidateOracle.Candidates.Select(static candidate =>
                    candidate.ScoreEndExclusive - candidate.ScoreStartInclusive)));

        double? baseControlDelta = null;
        double? controlFloor = null;
        double? sentinelFloor = null;
        double? controlSentinelDelta = tensorReferences.ControlSentinelMaximumDelta;
        double? candidateDelta = null;
        double? candidateLogProbabilityDelta = null;
        bool candidateWinnerStable = false;
        bool numericalIsolation = false;
        if (outputsValid && candidateOutputsValid)
        {
            bool baseExact = observations
                .Where(static observation => observation.State == "base")
                .All(observation => ExactTensorIdentity(baseReference, observation));
            bool controlExact = observations
                .Where(static observation => observation.State == "control-zero")
                .All(observation => ExactTensorIdentity(controlReference, observation));
            bool sentinelExact = observations
                .Where(static observation => observation.State == "sentinel-seeded")
                .All(observation => ExactTensorIdentity(sentinelReference, observation));
            if (!controlExact || !sentinelExact)
                controlSentinelDelta = null;
            baseControlDelta = baseExact
                && controlExact
                && ExactTensorIdentity(baseReference, controlReference)
                    ? 0.0
                    : null;
            controlFloor = controlExact ? 0.0 : null;
            sentinelFloor = sentinelExact ? 0.0 : null;
            QwenCandidateOracleObservation[] baseCandidateObservations =
                candidateObservations.Where(static observation =>
                    observation.State == "base").ToArray();
            QwenCandidateOracleObservation[] controlCandidateObservations =
                candidateObservations.Where(static observation =>
                    observation.State == "control-zero").ToArray();
            candidateDelta = MaximumCandidateScoreDelta(
                baseCandidateObservations,
                controlCandidateObservations,
                static candidate => candidate.Score);
            candidateLogProbabilityDelta = MaximumCandidateScoreDelta(
                baseCandidateObservations,
                controlCandidateObservations,
                static candidate => candidate.LogProbability);
            candidateWinnerStable = CandidateWinnerStable(
                baseCandidateObservations.Concat(controlCandidateObservations));
            double? repeatabilityFloor = controlFloor is not null && sentinelFloor is not null
                ? Math.Max(controlFloor.Value, sentinelFloor.Value)
                : null;
            numericalIsolation = baseControlDelta is <= 0.001
                && controlFloor is <= 0.001
                && sentinelFloor is <= 0.001
                && controlSentinelDelta is >= 0.01
                && repeatabilityFloor is not null
                && controlSentinelDelta >= repeatabilityFloor * 10.0
                && candidateDelta <= 0.001
                && candidateLogProbabilityDelta <= 0.001
                && candidateWinnerStable;
        }
        QwenMeasured<bool> disposal = QwenAdapterResourceSampler.Measure(
            "runtime_dispose",
            () =>
            {
                runtime.Dispose();
                return true;
            });
        resources.Add(disposal.Transition);
        bool valid = runtime.ModelInstanceCount == 1
            && loads.All(static load => load.Loaded)
            && negatives.All(static negative =>
                negative.ExpectedFailureObserved && negative.RecoveryPassed)
            && outputsValid
            && candidateOutputsValid
            && numericalIsolation
            && runtime.CleanupFailures.Count == 0;

        return new QwenAdapterCompatibilityReport(
            plan.ExperimentId,
            plan.Phase,
            plan.FreshProcessOrdinal,
            plan.Provider,
            valid,
            valid ? ValidVerdict : InvalidRuntimeVerdict,
            runtime.ModelInstanceCount,
            loads.AsReadOnly(),
            negatives.AsReadOnly(),
            observations.Select(observation =>
                ToReport(observation, candidateObservations)).ToArray(),
            candidateObservations.AsReadOnly(),
            timings,
            resources.AsReadOnly(),
            runtime.CleanupFailures,
            null,
            null,
            baseControlDelta,
            controlFloor,
            sentinelFloor,
            controlSentinelDelta,
            candidateDelta,
            candidateLogProbabilityDelta,
            candidateWinnerStable,
            ClaimBoundary);
        }
        catch (Exception exception)
        {
            return EmptyReport(
                plan,
                InvalidRuntimeVerdict,
                Array.Empty<QwenAdapterLoadOutcome>(),
                runtime.ModelInstanceCount,
                runtime.CleanupFailures,
                exception.GetType().Name,
                "runtime_protocol",
                resources.AsReadOnly());
        }
    }

    internal static double MaximumAbsoluteDelta(
        QwenAdapterRuntimeObservation left,
        QwenAdapterRuntimeObservation right)
    {
        if (!left.Shape.SequenceEqual(right.Shape)
            || !string.Equals(left.DType, right.DType, StringComparison.Ordinal)
            || left.ElementCount != right.ElementCount
            || left.ComparisonValues is null
            || right.ComparisonValues is null
            || left.ComparisonValues.Length != right.ComparisonValues.Length)
            return double.PositiveInfinity;

        double maximum = 0.0;
        for (int i = 0; i < left.ComparisonValues.Length; i++)
            maximum = Math.Max(
                maximum,
                Math.Abs(
                    (float)left.ComparisonValues[i]
                    - (float)right.ComparisonValues[i]));
        return maximum;
    }

    private static QwenInitialTensorReferences CaptureInitialTensorReferences(
        IQwenAdapterProbeRuntime runtime,
        IReadOnlyList<int> promptTokenIds)
    {
        QwenAdapterRuntimeObservation baseReference = Observe(
            runtime,
            promptTokenIds,
            null,
            -3);
        QwenAdapterRuntimeObservation controlWithValues = Observe(
            runtime,
            promptTokenIds,
            "control-zero",
            -2,
            retainComparisonValues: true);
        QwenAdapterRuntimeObservation sentinelWithValues = Observe(
            runtime,
            promptTokenIds,
            "sentinel-seeded",
            -1,
            retainComparisonValues: true);
        double delta = MaximumAbsoluteDelta(controlWithValues, sentinelWithValues);
        return new QwenInitialTensorReferences(
            baseReference,
            Compact(controlWithValues),
            Compact(sentinelWithValues),
            double.IsFinite(delta) ? delta : null);
    }

    private static QwenAdapterRuntimeObservation Compact(
        QwenAdapterRuntimeObservation observation) =>
        observation with { ComparisonValues = null };

    private static bool IsValidCompactObservation(
        QwenAdapterRuntimeObservation observation,
        IReadOnlyList<long> expectedShape) =>
        observation.Finite
        && string.Equals(observation.DType, "float16", StringComparison.Ordinal)
        && observation.Shape.SequenceEqual(expectedShape)
        && observation.ElementCount == Product(observation.Shape)
        && observation.ComparisonValues is null;

    private static bool ExactTensorIdentity(
        QwenAdapterRuntimeObservation expected,
        QwenAdapterRuntimeObservation actual) =>
        expected.Finite
        && actual.Finite
        && expected.ElementCount == actual.ElementCount
        && expected.Shape.SequenceEqual(actual.Shape)
        && string.Equals(expected.DType, actual.DType, StringComparison.Ordinal)
        && string.Equals(expected.Sha256, actual.Sha256, StringComparison.Ordinal);

    private static bool CandidateOracleMatchesTokenizer(
        IQwenAdapterProbeRuntime runtime,
        QwenAdapterCompatibilityPlan plan)
    {
        const int bosTokenId = 151643;
        IReadOnlyList<int> encodedPrompt = runtime.Encode(plan.Prompt);
        if (plan.CandidateOracle.PromptTokenIds.Count != encodedPrompt.Count + 1
            || plan.CandidateOracle.PromptTokenIds[0] != bosTokenId
            || !plan.CandidateOracle.PromptTokenIds.Skip(1).SequenceEqual(encodedPrompt))
            return false;

        var completions = new int[plan.CandidateOracle.Candidates.Count][];
        for (int index = 0; index < plan.CandidateOracle.Candidates.Count; index++)
        {
            QwenAdapterCandidateCompletionPlan candidate =
                plan.CandidateOracle.Candidates[index];
            int[] encoded = runtime.Encode(candidate.Text + "\n").ToArray();
            if (encoded.Length > 0 && encoded[0] == bosTokenId)
                encoded = encoded[1..];
            if (!candidate.CompletionTokenIds.SequenceEqual(encoded))
                return false;
            completions[index] = encoded;
        }

        CandidateCompletionPlan[] spans = CandidateCompletionPlan.Create(completions);
        return spans.Length == plan.CandidateOracle.Candidates.Count
            && spans.Select(static span => (span.Start, span.EndExclusive))
                .SequenceEqual(plan.CandidateOracle.Candidates.Select(static candidate =>
                    (candidate.ScoreStartInclusive, candidate.ScoreEndExclusive)));
    }

    private static QwenAdapterRuntimeObservation Observe(
        IQwenAdapterProbeRuntime runtime,
        IReadOnlyList<int> promptTokenIds,
        string? adapterName,
        int ordinal,
        bool retainComparisonValues = false)
    {
        using IQwenAdapterProbeRequest request = runtime.CreateRequest(promptTokenIds);
        if (adapterName is not null)
            _ = request.SetActiveAdapter(adapterName);
        return request.Execute(
            adapterName ?? "base",
            ordinal,
            retainComparisonValues);
    }

    private static QwenCandidateOracleObservation ScoreCandidates(
        IQwenAdapterProbeRuntime runtime,
        QwenAdapterCompatibilityPlan plan,
        string? adapterName,
        int ordinal)
    {
        var scores = new List<QwenForcedCandidateScore>(
            plan.CandidateOracle.Candidates.Count);
        foreach (QwenAdapterCandidateCompletionPlan candidate in
            plan.CandidateOracle.Candidates)
        {
            int promptCount = plan.CandidateOracle.PromptTokenIds.Count;
            var input = new int[promptCount + candidate.ScoreEndExclusive];
            for (int index = 0; index < promptCount; index++)
                input[index] = plan.CandidateOracle.PromptTokenIds[index];
            for (int index = 0; index < candidate.ScoreEndExclusive; index++)
                input[promptCount + index] = candidate.CompletionTokenIds[index];

            using IQwenAdapterProbeRequest request = runtime.CreateRequest(input);
            if (adapterName is not null)
                _ = request.SetActiveAdapter(adapterName);
            scores.Add(request.ScoreCandidate(
                candidate.Id,
                promptCount,
                candidate.CompletionTokenIds,
                candidate.ScoreStartInclusive,
                candidate.ScoreEndExclusive));
        }

        return new QwenCandidateOracleObservation(
            adapterName ?? "base",
            ordinal,
            scores.AsReadOnly(),
            WinnerIndex(scores.Select(static candidate => candidate.Score).ToArray()));
    }

    private static IReadOnlyList<QwenAdapterTimingSeries> RunTimingProtocol(
        IQwenAdapterProbeRuntime runtime,
        QwenAdapterCompatibilityPlan plan,
        QwenResourceTransition modelLoad,
        ICollection<QwenAdapterRuntimeObservation> observations)
    {
        var samples = new Dictionary<string, List<QwenAdapterTimingSample>>(
            StringComparer.Ordinal)
        {
            ["model_load"] =
            [
                new QwenAdapterTimingSample(
                    modelLoad.OperationWallMilliseconds,
                    modelLoad.ProcessCpuDeltaMilliseconds,
                    modelLoad.CurrentThreadAllocatedBytes),
            ],
        };

        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
        {
            QwenAdapterResourceSampler.CollectFullGarbage();
            string prefix = adapter.Name;
            List<QwenAdapterTimingSample> load = samples[$"{prefix}_load"] = [];
            List<QwenAdapterTimingSample> unload = samples[$"{prefix}_unload"] = [];
            for (int iteration = 0; iteration < 110; iteration++)
            {
                QwenAdapterTimingSample loadSample = MeasureTiming(
                    () => runtime.LoadAdapter(adapter.Path, adapter.Name));
                QwenAdapterTimingSample unloadSample = MeasureTiming(
                    () => runtime.UnloadAdapter(adapter.Name));

                if (iteration >= 10)
                {
                    load.Add(loadSample);
                    unload.Add(unloadSample);
                }
            }
        }

        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
            runtime.LoadAdapter(adapter.Path, adapter.Name);

        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
        {
            QwenAdapterResourceSampler.CollectFullGarbage();
            List<QwenAdapterTimingSample> generator =
                samples[$"{adapter.Name}_generator_create"] = [];
            List<QwenAdapterTimingSample> activation =
                samples[$"{adapter.Name}_activation"] = [];
            for (int iteration = 0; iteration < 110; iteration++)
            {
                (IQwenAdapterProbeRequest request, QwenAdapterTimingSample createSample) =
                    MeasureTiming(() => runtime.CreateRequest(
                        plan.CandidateOracle.PromptTokenIds));
                using (request)
                {
                    QwenAdapterTimingSample activationSample = MeasureTiming(
                        () => request.SetActiveAdapter(adapter.Name)).Sample;
                    if (iteration >= 10)
                    {
                        generator.Add(createSample);
                        activation.Add(activationSample);
                    }
                }
            }
        }

        (string State, string? Adapter)[] states =
        [
            ("base", null),
            ("control-zero", "control-zero"),
            ("sentinel-seeded", "sentinel-seeded"),
        ];
        int ordinal = 1_000;
        foreach ((string state, string? adapterName) in states)
        {
            QwenAdapterResourceSampler.CollectFullGarbage();
            List<QwenAdapterTimingSample> firstForward =
                samples[$"{state}_first_forward"] = [];
            List<QwenAdapterTimingSample> activationToFirst =
                samples[$"{state}_activation_to_first"] = [];
            List<QwenAdapterTimingSample> complete =
                samples[$"{state}_complete_one_forward"] = [];
            for (int iteration = 0; iteration < 23; iteration++)
            {
                (IQwenAdapterProbeRequest request, QwenAdapterTimingSample createSample) =
                    MeasureTiming(() => runtime.CreateRequest(
                        plan.CandidateOracle.PromptTokenIds));
                using (request)
                {
                    QwenAdapterTimingSample activationSample = adapterName is null
                        ? new QwenAdapterTimingSample(0.0, 0.0, 0)
                        : MeasureTiming(() => request.SetActiveAdapter(adapterName)).Sample;
                    (QwenAdapterRuntimeObservation observation,
                        QwenAdapterTimingSample forwardSample) = MeasureTiming(
                            () => request.Execute(state, ordinal++));
                    if (iteration >= 3)
                    {
                        observations.Add(observation);
                        firstForward.Add(forwardSample);
                        activationToFirst.Add(Add(activationSample, forwardSample));
                        complete.Add(Add(createSample, activationSample, forwardSample));
                    }
                }
            }
        }

        runtime.UnloadAdapter("control-zero");
        runtime.UnloadAdapter("sentinel-seeded");

        return samples.Select(static pair => Distribution(pair.Key, pair.Value)).ToArray();
    }

    private static void RunLoadNegative(
        IQwenAdapterProbeRuntime runtime,
        IReadOnlyList<int> promptTokenIds,
        string id,
        string path,
        string name,
        string expectedExceptionType,
        QwenAdapterRuntimeObservation reference,
        ICollection<QwenAdapterNegativeOutcome> outcomes)
    {
        Exception? failure = null;
        try
        {
            runtime.LoadAdapter(path, name);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        bool recovery = IsWithinTolerance(
            reference,
            Observe(runtime, promptTokenIds, "control-zero", int.MinValue));
        outcomes.Add(new QwenAdapterNegativeOutcome(
            id,
            expectedExceptionType,
            string.Equals(
                failure?.GetType().Name,
                expectedExceptionType,
                StringComparison.Ordinal),
            failure?.GetType().Name,
            recovery));

        if (failure is null && !string.Equals(name, "control-zero", StringComparison.Ordinal))
        {
            try
            {
                runtime.UnloadAdapter(name);
            }
            catch
            {
                // The invalid verdict already captures the unexpected success.
            }
        }
    }

    private static void RunUnloadLifecycle(
        IQwenAdapterProbeRuntime runtime,
        IReadOnlyList<int> promptTokenIds,
        QwenAdapterRuntimeObservation controlReference,
        QwenAdapterRuntimeObservation sentinelReference,
        ICollection<QwenAdapterRuntimeObservation> observations,
        ICollection<QwenAdapterNegativeOutcome> outcomes)
    {
        Exception? inUseFailure = null;
        bool liveRecovery;
        using (IQwenAdapterProbeRequest request = runtime.CreateRequest(promptTokenIds))
        {
            _ = request.SetActiveAdapter("control-zero");
            try
            {
                runtime.UnloadAdapter("control-zero");
            }
            catch (Exception exception)
            {
                inUseFailure = exception;
            }

            QwenAdapterRuntimeObservation observed = request.Execute(
                "control-zero",
                FrozenStateOrder.Length);
            observations.Add(observed);
            liveRecovery = IsWithinTolerance(controlReference, observed);
        }

        outcomes.Add(new QwenAdapterNegativeOutcome(
            "unload-while-referenced",
            "OnnxRuntimeGenAIException",
            string.Equals(
                inUseFailure?.GetType().Name,
                "OnnxRuntimeGenAIException",
                StringComparison.Ordinal),
            inUseFailure?.GetType().Name,
            liveRecovery));

        runtime.UnloadAdapter("control-zero");

        Exception? unloadedFailure = null;
        using (IQwenAdapterProbeRequest request = runtime.CreateRequest(promptTokenIds))
        {
            try
            {
                _ = request.SetActiveAdapter("control-zero");
            }
            catch (Exception exception)
            {
                unloadedFailure = exception;
            }
        }

        QwenAdapterRuntimeObservation sentinelRecovery = Observe(
            runtime,
            promptTokenIds,
            "sentinel-seeded",
            FrozenStateOrder.Length + 1);
        observations.Add(sentinelRecovery);
        outcomes.Add(new QwenAdapterNegativeOutcome(
            "activation-after-unload",
            "OnnxRuntimeGenAIException",
            string.Equals(
                unloadedFailure?.GetType().Name,
                "OnnxRuntimeGenAIException",
                StringComparison.Ordinal),
            unloadedFailure?.GetType().Name,
            IsWithinTolerance(sentinelReference, sentinelRecovery)));

        runtime.UnloadAdapter("sentinel-seeded");
    }

    private static bool IsWithinTolerance(
        QwenAdapterRuntimeObservation expected,
        QwenAdapterRuntimeObservation actual) =>
        ExactTensorIdentity(expected, actual);

    private static QwenAdapterObservationReport ToReport(
        QwenAdapterRuntimeObservation observation,
        IReadOnlyList<QwenCandidateOracleObservation> candidateObservations)
    {
        QwenCandidateOracleObservation? oracle = candidateObservations.FirstOrDefault(
            candidate => candidate.Ordinal == observation.Ordinal
                && string.Equals(
                    candidate.State,
                    observation.State,
                    StringComparison.Ordinal));
        double[] scores = oracle?.Candidates.Select(static candidate => candidate.Score)
            .ToArray() ?? [];
        return new(
            observation.State,
            observation.Ordinal,
            observation.Sha256,
            observation.Shape,
            observation.DType,
            observation.Finite,
            observation.GeneratorCreateMilliseconds,
            observation.ActivationMilliseconds,
            observation.FirstForwardMilliseconds,
            scores,
            oracle?.WinnerIndex ?? -1);
    }

    private static double MaximumCandidateScoreDelta(
        IEnumerable<QwenCandidateOracleObservation> left,
        IEnumerable<QwenCandidateOracleObservation> right,
        Func<QwenForcedCandidateScore, double> select)
    {
        double maximum = 0.0;
        QwenCandidateOracleObservation[] rightArray = right.ToArray();
        foreach (QwenCandidateOracleObservation leftObservation in left)
        {
            foreach (QwenCandidateOracleObservation rightObservation in rightArray)
            {
                if (leftObservation.Candidates.Count != rightObservation.Candidates.Count)
                    return double.PositiveInfinity;
                for (int i = 0; i < leftObservation.Candidates.Count; i++)
                {
                    QwenForcedCandidateScore leftCandidate = leftObservation.Candidates[i];
                    QwenForcedCandidateScore rightCandidate = rightObservation.Candidates[i];
                    if (!string.Equals(
                            leftCandidate.Id,
                            rightCandidate.Id,
                            StringComparison.Ordinal)
                        || leftCandidate.ScoredTokenCount != rightCandidate.ScoredTokenCount)
                        return double.PositiveInfinity;
                    maximum = Math.Max(
                        maximum,
                        Math.Abs(select(leftCandidate) - select(rightCandidate)));
                }
            }
        }
        return maximum;
    }

    private static bool CandidateWinnerStable(
        IEnumerable<QwenCandidateOracleObservation> observations)
    {
        int[] winners = observations
            .Select(static observation => observation.WinnerIndex)
            .Distinct()
            .ToArray();
        return winners.Length == 1;
    }

    private static int WinnerIndex(IReadOnlyList<double> scores)
    {
        int winner = 0;
        for (int i = 1; i < scores.Count; i++)
            if (scores[i] > scores[winner])
                winner = i;
        return winner;
    }

    private static long Product(IReadOnlyList<long> shape)
    {
        long product = 1;
        foreach (long dimension in shape)
            product = checked(product * dimension);
        return product;
    }

    private static QwenAdapterTimingSeries Distribution(
        string name,
        IReadOnlyList<QwenAdapterTimingSample> raw)
    {
        double[] ordered = raw.Select(static sample => sample.WallMilliseconds)
            .Order().ToArray();
        return new QwenAdapterTimingSeries(
            name,
            raw.ToArray(),
            NearestRank(ordered, 0.50),
            NearestRank(ordered, 0.95),
            NearestRank(ordered, 0.99),
            ordered[^1]);
    }

    private static QwenAdapterTimingSample MeasureTiming(Action action)
    {
        (bool _, QwenAdapterTimingSample sample) = MeasureTiming(() =>
        {
            action();
            return true;
        });
        return sample;
    }

    private static (T Value, QwenAdapterTimingSample Sample) MeasureTiming<T>(Func<T> action)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        T value = action();
        double wall = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        process.Refresh();
        double cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        return (value, new QwenAdapterTimingSample(wall, cpu, allocated));
    }

    private static QwenAdapterTimingSample Add(
        params QwenAdapterTimingSample[] samples) =>
        new(
            samples.Sum(static sample => sample.WallMilliseconds),
            samples.Sum(static sample => sample.ProcessCpuMilliseconds),
            samples.Sum(static sample => sample.CurrentThreadAllocatedBytes));

    private static double NearestRank(IReadOnlyList<double> ordered, double percentile)
    {
        int index = Math.Max(0, (int)Math.Ceiling(percentile * ordered.Count) - 1);
        return ordered[index];
    }

    private static QwenAdapterCompatibilityReport EmptyReport(
        QwenAdapterCompatibilityPlan plan,
        string verdict,
        IReadOnlyList<QwenAdapterLoadOutcome> loads,
        int modelInstanceCount = 0,
        IReadOnlyList<string>? cleanupFailures = null,
        string? fatalExceptionType = null,
        string? fatalStage = null,
        IReadOnlyList<QwenResourceTransition>? resources = null) =>
        new(
            plan.ExperimentId,
            plan.Phase,
            plan.FreshProcessOrdinal,
            plan.Provider,
            false,
            verdict,
            modelInstanceCount,
            loads,
            Array.Empty<QwenAdapterNegativeOutcome>(),
            Array.Empty<QwenAdapterObservationReport>(),
            Array.Empty<QwenCandidateOracleObservation>(),
            Array.Empty<QwenAdapterTimingSeries>(),
            resources ?? Array.Empty<QwenResourceTransition>(),
            cleanupFailures ?? Array.Empty<string>(),
            fatalExceptionType,
            fatalStage,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            ClaimBoundary);

    public static QwenAdapterCompatibilityReport FatalReport(
        QwenAdapterCompatibilityPlan plan,
        Exception exception,
        string stage = "command") =>
        EmptyReport(
            plan,
            InvalidRuntimeVerdict,
            Array.Empty<QwenAdapterLoadOutcome>(),
            fatalExceptionType: exception.GetType().Name,
            fatalStage: stage);
}
