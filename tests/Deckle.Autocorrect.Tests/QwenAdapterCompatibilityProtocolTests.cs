using Deckle.Autocorrect.Probe;
using Microsoft.ML.OnnxRuntimeGenAI;
using System.Text.Json;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class QwenAdapterCompatibilityProtocolTests
{
    [Fact]
    public void AcceptsOnlyTheFrozenPhaseAPlanShape()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan(readerValid: true);

        QwenAdapterPlanVerdict verdict =
            QwenAdapterCompatibilityPlanReader.Evaluate(plan);

        Assert.True(verdict.Accepted);
        Assert.Equal(QwenAdapterPlanVerdict.AcceptedCode, verdict.Code);
    }

    [Fact]
    public void RejectsProtocolBasePathAdapterAndNegativeDrift()
    {
        QwenAdapterCompatibilityPlan exact = FrozenPlan(readerValid: true);
        (string Code, QwenAdapterCompatibilityPlan Plan)[] cases =
        [
            (
                QwenAdapterPlanVerdict.UnsupportedProtocolCode,
                exact with { Provider = "dml" }
            ),
            (
                QwenAdapterPlanVerdict.InvalidPathCode,
                exact with { ModelDirectory = "relative" }
            ),
            (
                QwenAdapterPlanVerdict.InvalidBaseContractCode,
                exact with { Base = exact.Base with { Rank = 16 } }
            ),
            (
                QwenAdapterPlanVerdict.InvalidBaseContractCode,
                exact with
                {
                    CandidateOracle = exact.CandidateOracle with
                    {
                        Candidates =
                        [
                            exact.CandidateOracle.Candidates[0] with
                            {
                                Text = "je suis ici",
                            },
                            exact.CandidateOracle.Candidates[1],
                        ],
                    },
                }
            ),
            (
                QwenAdapterPlanVerdict.InvalidAdapterSetCode,
                exact with { Adapters = exact.Adapters.Reverse().ToArray() }
            ),
            (
                QwenAdapterPlanVerdict.InvalidNegativeSetCode,
                exact with { RuntimeNegatives = Array.Empty<QwenAdapterRuntimeNegativePlan>() }
            ),
        ];

        foreach ((string code, QwenAdapterCompatibilityPlan plan) in cases)
        {
            QwenAdapterPlanVerdict verdict =
                QwenAdapterCompatibilityPlanReader.Evaluate(plan);
            Assert.False(verdict.Accepted);
            Assert.Equal(code, verdict.Code);
        }
    }

    [Fact]
    public void RefusesStructurallyIncompleteJsonWithoutThrowing()
    {
        QwenAdapterCompatibilityPlan exact = FrozenPlan(readerValid: true);
        QwenAdapterCompatibilityPlan[] incomplete =
        [
            exact with { ArtifactManifest = exact.ArtifactManifest with { Sha256 = null! } },
            exact with { ExpectedLogitsShape = null! },
            exact with
            {
                Base = exact.Base with { TargetModules = null! },
            },
            exact with
            {
                CandidateOracle = exact.CandidateOracle with
                {
                    Candidates = [null!, exact.CandidateOracle.Candidates[1]],
                },
            },
            exact with { Adapters = [null!, exact.Adapters[1]] },
            exact with
            {
                RuntimeNegatives =
                [null!, .. exact.RuntimeNegatives.Skip(1)],
            },
        ];
        string path = Path.Combine(
            Path.GetTempPath(),
            $"acx-0023-incomplete-{Guid.NewGuid():N}.json");
        try
        {
            foreach (QwenAdapterCompatibilityPlan candidate in incomplete)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(candidate));
                (QwenAdapterCompatibilityPlan? plan, QwenAdapterPlanVerdict verdict) =
                    QwenAdapterCompatibilityPlanReader.TryRead(path);
                Assert.Null(plan);
                Assert.False(verdict.Accepted);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParsesOnlyTheDedicatedPlanCommandShape()
    {
        string path = Absolute("plan.json");

        ProbeArguments? parsed = ProbeArguments.Parse(
            ["--qwen-adapter-compatibility", "--plan", path]);

        Assert.NotNull(parsed);
        Assert.Equal(ProbeMode.QwenAdapterCompatibility, parsed.Mode);
        Assert.Equal(path, parsed.PlanPath);
        Assert.Null(ProbeArguments.Parse(["--qwen-adapter-compatibility"]));
        Assert.Null(ProbeArguments.Parse(
            ["--qwen-adapter-compatibility", "--plan", path, "--provider", "cpu"]));
        Assert.Null(ProbeArguments.Parse(["--plan", path, "--candidate", "a"]));

        ProbeArguments? crossModel = ProbeArguments.Parse(
            ["--qwen-adapter-cross-model", "--plan", path]);
        Assert.NotNull(crossModel);
        Assert.Equal(ProbeMode.QwenAdapterCrossModel, crossModel.Mode);
    }

    [Fact]
    public void RunsOneModelWithFreshRequestsAndIdentityStableAlternation()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory();
        IReadOnlyDictionary<string, QwenAdapterManifest> manifests =
            ExactManifests(plan);

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(plan, manifests);

        Assert.True(report.Valid, JsonSerializer.Serialize(report));
        Assert.Equal(QwenAdapterCompatibilityRunner.ValidVerdict, report.Verdict);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(1, report.ModelInstanceCount);
        Assert.Equal(2, report.Loads.Count);
        Assert.All(report.Loads, static load => Assert.True(load.Loaded));
        Assert.Equal(11, report.Negatives.Count);
        Assert.All(report.Negatives, static negative =>
        {
            Assert.True(negative.ExpectedFailureObserved);
            Assert.True(negative.RecoveryPassed);
        });
        Assert.Equal(74, report.Observations.Count);
        Assert.Equal(346, factory.Runtime!.CreateRequestCalls);
        Assert.Equal(346, factory.Runtime.DisposedRequestCalls);
        Assert.Equal(2, factory.Runtime.RetainedComparisonRequestCalls);
        Assert.Equal(15, report.CandidateOracleObservations.Count);
        Assert.All(report.CandidateOracleObservations, static observation =>
        {
            Assert.Equal(["literal", "corrected"],
                observation.Candidates.Select(static candidate => candidate.Id));
            Assert.All(observation.Candidates, static candidate =>
                Assert.Equal(2, candidate.ScoredTokenCount));
            Assert.Equal(1, observation.WinnerIndex);
        });
        Assert.Equal(18, report.Timings.Count);
        Assert.Equal(
            100,
            report.Timings.Single(static series =>
                series.Name == "control-zero_activation").RawSamples.Count);
        Assert.Equal(
            20,
            report.Timings.Single(static series =>
                series.Name == "sentinel-seeded_first_forward").RawSamples.Count);
        Assert.Equal(0.0, report.BaseControlMaximumAbsoluteDelta);
        Assert.Equal(0.0, report.ControlRepeatabilityMaximumAbsoluteDelta);
        Assert.Equal(0.0, report.SentinelRepeatabilityMaximumAbsoluteDelta);
        Assert.Equal(0.125, report.ControlSentinelMinimumMaximumAbsoluteDelta);
        Assert.Equal(0.0, report.BaseControlCandidateScoreMaximumAbsoluteDelta);
        Assert.Equal(0.0, report.BaseControlCandidateLogProbabilityMaximumAbsoluteDelta);
        Assert.True(report.BaseControlCandidateWinnerStable);
        Assert.Equal(
            [
                "base",
                "control-zero",
                "sentinel-seeded",
                "control-zero",
                "sentinel-seeded",
                "control-zero",
                "base",
                "sentinel-seeded",
                "control-zero",
                "sentinel-seeded",
                "control-zero",
                "sentinel-seeded",
            ],
            report.Observations.Take(12).Select(static observation => observation.State));
        Assert.True(factory.Runtime.Disposed);
        Assert.Empty(factory.Runtime.LoadedNames);
    }

    [Fact]
    public void RefusesCandidateTokensThatDoNotMatchTheFrozenLiteral()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory { CorruptCandidateEncoding = true };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.Equal(QwenAdapterCompatibilityRunner.InvalidRuntimeVerdict, report.Verdict);
        Assert.Empty(report.Loads);
        Assert.NotNull(factory.Runtime);
        Assert.Empty(factory.Runtime.LoadedNames);
    }

    [Fact]
    public void RefusesAManifestBeforeConstructingTheRuntime()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var manifests = new Dictionary<string, QwenAdapterManifest>(
            ExactManifests(plan),
            StringComparer.Ordinal)
        {
            ["sentinel-seeded"] = plan.Base.ToManifest() with
            {
                GraphSha256 = new string('b', 64),
            },
        };
        var factory = new FakeRuntimeFactory();

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(plan, manifests);

        Assert.False(report.Valid);
        Assert.Equal(QwenAdapterCompatibilityRunner.InvalidManifestVerdict, report.Verdict);
        Assert.Equal(0, factory.CreateCalls);
        Assert.Empty(report.Loads);
    }

    [Fact]
    public void TreatsShapeDriftAsAnInfiniteNumericalMismatch()
    {
        QwenAdapterRuntimeObservation left = Observation("base", [1.0f, 2.0f]);
        QwenAdapterRuntimeObservation right = left with { Shape = [1, 1, 2] };

        double delta = QwenAdapterCompatibilityRunner.MaximumAbsoluteDelta(left, right);

        Assert.True(double.IsPositiveInfinity(delta));
    }

    [Fact]
    public void RejectsWrongLogitShapeWithoutSerializingInfinity()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory { CorruptSentinelShape = true };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));
        string json = QwenAdapterCompatibilityPlanReader.SerializeReport(report);

        Assert.False(report.Valid);
        Assert.Null(report.ControlSentinelMinimumMaximumAbsoluteDelta);
        Assert.DoesNotContain("Infinity", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearsSeededSeparationEvidenceAfterALateFingerprintDrift()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory { DriftLateSentinelFingerprint = true };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.Null(report.ControlSentinelMinimumMaximumAbsoluteDelta);
    }

    [Fact]
    public void RejectsAForeignNegativeFailureFamily()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory { ForeignNegativeException = true };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.Contains(report.Negatives, static negative =>
            !negative.ExpectedFailureObserved
            && negative.ExceptionType == nameof(InvalidDataException));
    }

    [Fact]
    public void UnloadsAPartialAdapterSetBeforeRuntimeDisposal()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory { FailSentinelLoad = true };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.True(factory.Runtime!.Disposed);
        Assert.Empty(factory.Runtime.LoadedNames);
    }

    [Fact]
    public void RetainsCleanupFailuresAfterAnEarlyPartialLoadReport()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory
        {
            FailSentinelLoad = true,
            FailRuntimeCleanup = true,
        };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.Contains("runtime_dispose:InvalidOperationException", report.CleanupFailures);
    }

    [Fact]
    public void RetainsFatalStageAndLaterCleanupFailures()
    {
        QwenAdapterCompatibilityPlan plan = FrozenPlan();
        var factory = new FakeRuntimeFactory
        {
            FailEncode = true,
            FailRuntimeCleanup = true,
        };

        QwenAdapterCompatibilityReport report =
            new QwenAdapterCompatibilityRunner(factory).Run(
                plan,
                ExactManifests(plan));

        Assert.False(report.Valid);
        Assert.Equal("InvalidOperationException", report.FatalExceptionType);
        Assert.Equal("runtime_protocol", report.FatalStage);
        Assert.Contains("runtime_dispose:InvalidOperationException", report.CleanupFailures);
    }

    [Fact]
    public void MatchesActualBytesInsteadOfTrustingADeclaredPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"acx-0023-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "artifact.bin");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var exact = new QwenArtifactIdentity(
                path,
                4,
                "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a");

            Assert.True(QwenAdapterArtifactGate.MatchesIdentity(exact));
            Assert.False(QwenAdapterArtifactGate.MatchesIdentity(
                exact with { Sha256 = new string('a', 64) }));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void ResolvesOnlyTheExistingGraphReferencedByGenAiConfig()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"acx-0023-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string graph = Path.Combine(directory, "model.onnx");
        string config = Path.Combine(directory, "genai_config.json");
        try
        {
            File.WriteAllBytes(graph, [1]);
            File.WriteAllText(
                config,
                "{\"model\":{\"decoder\":{\"filename\":\"model.onnx\"}}}");

            Assert.True(QwenAdapterArtifactGate.TryResolveConfiguredGraph(
                directory,
                out string configured));
            Assert.Equal(Path.GetFullPath(graph), configured);

            File.WriteAllText(
                config,
                "{\"model\":{\"decoder\":{\"filename\":\"../outside.onnx\"}}}");
            Assert.False(QwenAdapterArtifactGate.TryResolveConfiguredGraph(
                directory,
                out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ComputesTeacherForcedLogProbabilityWithStableLogSoftmax()
    {
        double uniform = QwenCandidateScoringMath.LogProbability([0.0f, 0.0f], 0);
        double shifted = QwenCandidateScoringMath.LogProbability([1000.0f, 999.0f], 0);

        Assert.Equal(-Math.Log(2.0), uniform, precision: 12);
        Assert.Equal(-Math.Log(1.0 + Math.Exp(-1.0)), shifted, precision: 12);
        Assert.Throws<InvalidDataException>(() =>
            QwenCandidateScoringMath.LogProbability([float.NaN, 0.0f], 0));
    }

    private static QwenAdapterCompatibilityPlan FrozenPlan(bool readerValid = false) =>
        new(
            1,
            "ACX-0023",
            "A",
            1,
            "cpu",
            Absolute("model"),
            "Le projet est la.",
            Absolute("report.json"),
            Absolute("cross-model-report.json"),
            new QwenArtifactIdentity(
                Absolute("artifacts.json"),
                100,
                new string('a', 64)),
            new QwenArtifactIdentity(
                Absolute("graph-verification.json"),
                100,
                new string('a', 64)),
            new QwenArtifactIdentity(
                Absolute("negative-verification.json"),
                100,
                new string('a', 64)),
            [1, 2, readerValid ? 151936 : 2],
            new QwenAdapterCandidateOraclePlan(
                [151643, 0],
                [
                    new QwenAdapterCandidateCompletionPlan(
                        "literal",
                        "je suis la",
                        [0, 1],
                        0,
                        2),
                    new QwenAdapterCandidateCompletionPlan(
                        "corrected",
                        "je suis là",
                        [1, 0],
                        0,
                        2),
                ]),
            new QwenAdapterBaseContract(
                "Qwen/Qwen3-0.6B",
                "c1899de289a04d12100db370d81485cdf75e47ca",
                new string('a', 64),
                ["q_proj", "v_proj"],
                28,
                1024,
                2048,
                1024,
                8,
                "float16"),
            [
                new QwenAdapterArtifactPlan(
                    "control-zero",
                    Absolute("control.onnx_adapter"),
                    Absolute("control.manifest.json")),
                new QwenAdapterArtifactPlan(
                    "sentinel-seeded",
                    Absolute("sentinel.onnx_adapter"),
                    Absolute("sentinel.manifest.json")),
            ],
            [
                new QwenAdapterRuntimeNegativePlan(
                    "missing-file",
                    Absolute("missing.onnx_adapter"),
                    "negative-missing-file",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "truncated-file",
                    Absolute("truncated.onnx_adapter"),
                    "negative-truncated-file",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "wrong-model-version",
                    Absolute("wrong-model-version.onnx_adapter"),
                    "negative-wrong-model-version",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "wrong-target-name",
                    Absolute("wrong-target-name.onnx_adapter"),
                    "negative-wrong-target-name",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "wrong-rank-shape",
                    Absolute("wrong-rank-shape.onnx_adapter"),
                    "negative-wrong-rank-shape",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "wrong-dtype",
                    Absolute("wrong-dtype.onnx_adapter"),
                    "negative-wrong-dtype",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "missing-tensor",
                    Absolute("missing-tensor.onnx_adapter"),
                    "negative-missing-tensor",
                    "OnnxRuntimeGenAIException"),
                new QwenAdapterRuntimeNegativePlan(
                    "extra-tensor",
                    Absolute("extra-tensor.onnx_adapter"),
                    "negative-extra-tensor",
                    "OnnxRuntimeGenAIException"),
            ]);

    private static IReadOnlyDictionary<string, QwenAdapterManifest> ExactManifests(
        QwenAdapterCompatibilityPlan plan) =>
        new Dictionary<string, QwenAdapterManifest>(StringComparer.Ordinal)
        {
            ["control-zero"] = plan.Base.ToManifest(),
            ["sentinel-seeded"] = plan.Base.ToManifest(),
        };

    private static QwenAdapterRuntimeObservation Observation(
        string state,
        float[] values,
        int ordinal = 0,
        bool retainComparisonValues = true) =>
        new(
            state,
            ordinal,
            state == "sentinel-seeded" ? "sentinel" : "base-control",
            [1, 2, values.Length / 2],
            "float16",
            true,
            0.1,
            state == "base" ? 0.0 : 0.01,
            1.0,
            values.Length,
            retainComparisonValues
                ? values.Select(static value => (Half)value).ToArray()
                : null);

    private static string Absolute(string name) =>
        Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "acx-0023", name);

    private sealed class FakeRuntimeFactory : IQwenAdapterProbeRuntimeFactory
    {
        public int CreateCalls { get; private set; }
        public FakeRuntime? Runtime { get; private set; }
        public bool CorruptSentinelShape { get; init; }
        public bool ForeignNegativeException { get; init; }
        public bool FailSentinelLoad { get; init; }
        public bool FailRuntimeCleanup { get; init; }
        public bool FailEncode { get; init; }
        public bool CorruptCandidateEncoding { get; init; }
        public bool DriftLateSentinelFingerprint { get; init; }

        public IQwenAdapterProbeRuntime Create(QwenAdapterCompatibilityPlan plan)
        {
            CreateCalls++;
            Runtime = new FakeRuntime(
                CorruptSentinelShape,
                ForeignNegativeException,
                FailSentinelLoad,
                FailRuntimeCleanup,
                FailEncode,
                CorruptCandidateEncoding,
                DriftLateSentinelFingerprint);
            return Runtime;
        }
    }

    private sealed class FakeRuntime : IQwenAdapterProbeRuntime
    {
        private readonly Dictionary<string, int> _references = new(StringComparer.Ordinal);
        private readonly bool _corruptSentinelShape;
        private readonly bool _foreignNegativeException;
        private readonly bool _failSentinelLoad;
        private readonly bool _failRuntimeCleanup;
        private readonly bool _failEncode;
        private readonly bool _corruptCandidateEncoding;
        private readonly bool _driftLateSentinelFingerprint;
        private bool _lateSentinelDriftInjected;
        private readonly List<string> _cleanupFailures = [];

        public FakeRuntime(
            bool corruptSentinelShape,
            bool foreignNegativeException,
            bool failSentinelLoad,
            bool failRuntimeCleanup,
            bool failEncode,
            bool corruptCandidateEncoding,
            bool driftLateSentinelFingerprint)
        {
            _corruptSentinelShape = corruptSentinelShape;
            _foreignNegativeException = foreignNegativeException;
            _failSentinelLoad = failSentinelLoad;
            _failRuntimeCleanup = failRuntimeCleanup;
            _failEncode = failEncode;
            _corruptCandidateEncoding = corruptCandidateEncoding;
            _driftLateSentinelFingerprint = driftLateSentinelFingerprint;
        }

        public int ModelInstanceCount => 1;
        public double ModelLoadMilliseconds => 10.0;
        public IReadOnlyList<string> CleanupFailures => _cleanupFailures.AsReadOnly();
        public int CreateRequestCalls { get; private set; }
        public int DisposedRequestCalls { get; private set; }
        public int RetainedComparisonRequestCalls { get; private set; }
        public bool Disposed { get; private set; }
        public HashSet<string> LoadedNames { get; } = new(StringComparer.Ordinal);
        public List<string> ActivationOrder { get; } = [];

        public IReadOnlyList<int> Encode(string text)
        {
            if (_failEncode)
                throw new InvalidOperationException("Frozen encode failure.");
            if (_corruptCandidateEncoding && text.EndsWith("\n", StringComparison.Ordinal))
                return [0, 0];
            return text switch
            {
                "je suis la\n" => [0, 1],
                "je suis là\n" => [1, 0],
                _ => [0],
            };
        }

        public void LoadAdapter(string path, string name)
        {
            if (name.StartsWith("negative-", StringComparison.Ordinal))
            {
                if (_foreignNegativeException)
                    throw new InvalidDataException("Foreign failure family.");
                throw NativeFailure("Frozen negative artifact.");
            }
            if (_failSentinelLoad
                && string.Equals(name, "sentinel-seeded", StringComparison.Ordinal))
                throw NativeFailure("Frozen second-load failure.");
            if (!LoadedNames.Add(name))
                throw NativeFailure("Duplicate adapter name.");
            _references[name] = 0;
        }

        public IQwenAdapterProbeRequest CreateRequest(IReadOnlyList<int> tokens)
        {
            CreateRequestCalls++;
            return new FakeRequest(this);
        }

        public void UnloadAdapter(string name)
        {
            if (!LoadedNames.Contains(name))
                throw NativeFailure("Adapter is not loaded.");
            if (_references[name] > 0)
                throw NativeFailure("Adapter is still referenced.");
            LoadedNames.Remove(name);
            _references.Remove(name);
        }

        public void Dispose()
        {
            if (Disposed)
                return;
            LoadedNames.Clear();
            _references.Clear();
            if (_failRuntimeCleanup)
                _cleanupFailures.Add("runtime_dispose:InvalidOperationException");
            Disposed = true;
        }

        private sealed class FakeRequest : IQwenAdapterProbeRequest
        {
            private readonly FakeRuntime _runtime;
            private string? _active;
            private bool _disposed;

            public FakeRequest(FakeRuntime runtime)
            {
                _runtime = runtime;
            }

            public double GeneratorCreateMilliseconds => 0.1;

            public double SetActiveAdapter(string name)
            {
                if (!_runtime.LoadedNames.Contains(name))
                    throw NativeFailure("Adapter is not loaded.");
                _active = name;
                _runtime._references[name]++;
                _runtime.ActivationOrder.Add(name);
                return 0.01;
            }

            public QwenAdapterRuntimeObservation Execute(
                string state,
                int ordinal,
                bool retainComparisonValues = false)
            {
                if (retainComparisonValues)
                    _runtime.RetainedComparisonRequestCalls++;
                string actualState = _active ?? "base";
                float[] values = actualState == "sentinel-seeded"
                    ? [1.125f, 2.0f, 3.0f, 4.0f]
                    : [1.0f, 2.0f, 3.0f, 4.0f];
                QwenAdapterRuntimeObservation observation =
                    Observation(
                        actualState,
                        values,
                        ordinal,
                        retainComparisonValues);
                if (_runtime._driftLateSentinelFingerprint
                    && !_runtime._lateSentinelDriftInjected
                    && !retainComparisonValues
                    && actualState == "sentinel-seeded"
                    && ordinal >= 0)
                {
                    _runtime._lateSentinelDriftInjected = true;
                    observation = observation with { Sha256 = "sentinel-drift" };
                }
                return _runtime._corruptSentinelShape
                    && actualState == "sentinel-seeded"
                    ? observation with { Shape = [1, 1, 4] }
                    : observation;
            }

            public QwenForcedCandidateScore ScoreCandidate(
                string id,
                int promptTokenCount,
                IReadOnlyList<int> completionTokenIds,
                int scoreStartInclusive,
                int scoreEndExclusive)
            {
                string actualState = _active ?? "base";
                int count = scoreEndExclusive - scoreStartInclusive;
                double logProbability = string.Equals(id, "literal", StringComparison.Ordinal)
                    ? -2.0
                    : -1.0;
                if (string.Equals(actualState, "sentinel-seeded", StringComparison.Ordinal))
                    logProbability -= 0.25;
                return new QwenForcedCandidateScore(
                    id,
                    logProbability / count,
                    logProbability,
                    count,
                    true,
                    [1, promptTokenCount + scoreEndExclusive, 2]);
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_active is not null)
                    _runtime._references[_active]--;
                _runtime.DisposedRequestCalls++;
            }
        }
    }

    private static Exception NativeFailure(string message) =>
        (Exception)Activator.CreateInstance(
            typeof(OnnxRuntimeGenAIException),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [message],
            culture: null)!;
}
