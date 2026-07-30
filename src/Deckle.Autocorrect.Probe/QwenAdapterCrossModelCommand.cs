using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace Deckle.Autocorrect.Probe;

internal sealed record QwenAdapterCrossModelReport(
    string ExperimentId,
    string Phase,
    bool Valid,
    string ExpectedExceptionType,
    string? ObservedExceptionType,
    bool RecoveryPassed,
    double? PrePostMaximumAbsoluteDelta,
    string? FatalExceptionType,
    string? FatalStage,
    IReadOnlyList<string> CleanupFailures,
    string ClaimBoundary);

internal static class QwenAdapterCrossModelCommand
{
    private const string ExpectedExceptionType = "OnnxRuntimeGenAIException";

    public static int Run(ProbeArguments arguments)
    {
        (QwenAdapterCompatibilityPlan? plan, QwenAdapterPlanVerdict planVerdict) =
            QwenAdapterCompatibilityPlanReader.TryRead(arguments.PlanPath);
        if (plan is null)
        {
            Console.Error.WriteLine($"ACX-0023 plan refused: {planVerdict.Code}");
            return 2;
        }

        if (File.Exists(plan.CrossModelOutputPath)
            || !Directory.Exists(Path.GetDirectoryName(plan.CrossModelOutputPath)))
            return 2;

        QwenAdapterArtifactVerdict artifactVerdict =
            QwenAdapterArtifactGate.Evaluate(plan);
        if (!artifactVerdict.Accepted)
        {
            Console.Error.WriteLine(
                $"ACX-0023 artifact identity refused: {artifactVerdict.Code}");
            return 2;
        }

        QwenAdapterArtifactPlan control = plan.Adapters[0];
        QwenAdapterManifest expected = plan.Base.ToManifest();
        QwenAdapterManifest actual;
        try
        {
            actual = QwenAdapterCompatibilityPlanReader.ReadManifest(control.ManifestPath);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Manifest read failed: {exception.GetType().Name}");
            return 2;
        }

        if (!QwenAdapterManifestGate.Evaluate(expected, actual).Accepted)
            return 2;

        QwenAdapterCrossModelReport report = Execute(plan, control);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        using (var stream = new FileStream(
            plan.CrossModelOutputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(json);

        return report.Valid ? 0 : 1;
    }

    private static QwenAdapterCrossModelReport Execute(
        QwenAdapterCompatibilityPlan plan,
        QwenAdapterArtifactPlan control)
    {
        OgaHandle? ogaHandle = null;
        Config? firstConfig = null;
        Config? secondConfig = null;
        Model? firstModel = null;
        Model? secondModel = null;
        Adapters? adapters = null;
        GeneratorParams? foreignParams = null;
        Generator? foreignGenerator = null;
        bool loaded = false;
        Exception? observed = null;
        bool recovery = false;
        double? prePostDelta = null;
        string? fatalExceptionType = null;
        string? fatalStage = null;
        var cleanupFailures = new List<string>();
        try
        {
            ogaHandle = new OgaHandle();
            firstConfig = CpuConfig(
                plan.ModelDirectory,
                cleanupFailures,
                "first_config_partial_dispose");
            secondConfig = CpuConfig(
                plan.ModelDirectory,
                cleanupFailures,
                "second_config_partial_dispose");
            firstModel = new Model(firstConfig);
            secondModel = new Model(secondConfig);
            adapters = new Adapters(firstModel);
            adapters.LoadAdapter(control.Path, control.Name);
            loaded = true;

            CrossModelObservation before = ObserveAdaptedOutput(
                firstModel,
                adapters,
                control.Name,
                plan.CandidateOracle.PromptTokenIds,
                cleanupFailures,
                "before");

            foreignParams = new GeneratorParams(secondModel);
            foreignParams.SetSearchOption("max_length", 2.0);
            foreignGenerator = new Generator(secondModel, foreignParams);
            try
            {
                foreignGenerator.SetActiveAdapter(adapters, control.Name);
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            TryDispose(
                foreignGenerator,
                "foreign_generator_dispose",
                cleanupFailures);
            foreignGenerator = null;
            TryDispose(
                foreignParams,
                "foreign_params_dispose",
                cleanupFailures);
            foreignParams = null;
            CrossModelObservation after = ObserveAdaptedOutput(
                firstModel,
                adapters,
                control.Name,
                plan.CandidateOracle.PromptTokenIds,
                cleanupFailures,
                "after");
            prePostDelta = MaximumAbsoluteDelta(before, after);
            recovery = before.Finite
                && after.Finite
                && prePostDelta <= 0.001;
        }
        catch (Exception exception)
        {
            fatalExceptionType = exception.GetType().Name;
            fatalStage = "cross_model_protocol";
        }
        finally
        {
            TryDispose(foreignGenerator, "foreign_generator_dispose", cleanupFailures);
            TryDispose(foreignParams, "foreign_params_dispose", cleanupFailures);
            if (loaded && adapters is not null)
            {
                try
                {
                    adapters.UnloadAdapter(control.Name);
                }
                catch (Exception exception)
                {
                    recovery = false;
                    cleanupFailures.Add(
                        $"control_unload:{exception.GetType().Name}");
                }
            }
            TryDispose(adapters, "adapters_dispose", cleanupFailures);
            TryDispose(secondModel, "second_model_dispose", cleanupFailures);
            TryDispose(firstModel, "first_model_dispose", cleanupFailures);
            TryDispose(secondConfig, "second_config_dispose", cleanupFailures);
            TryDispose(firstConfig, "first_config_dispose", cleanupFailures);
            TryDispose(ogaHandle, "oga_dispose", cleanupFailures);
        }

        bool exactFailure = string.Equals(
            observed?.GetType().Name,
            ExpectedExceptionType,
            StringComparison.Ordinal);
        return new QwenAdapterCrossModelReport(
            plan.ExperimentId,
            plan.Phase,
            exactFailure
                && recovery
                && fatalExceptionType is null
                && cleanupFailures.Count == 0,
            ExpectedExceptionType,
            observed?.GetType().Name,
            recovery,
            prePostDelta,
            fatalExceptionType,
            fatalStage,
            cleanupFailures.AsReadOnly(),
            QwenAdapterCompatibilityRunner.ClaimBoundary);
    }

    private static CrossModelObservation ObserveAdaptedOutput(
        Model model,
        Adapters adapters,
        string adapterName,
        IReadOnlyList<int> tokens,
        ICollection<string> cleanupFailures,
        string stage)
    {
        GeneratorParams? parameters = null;
        Generator? generator = null;
        Tensor? logits = null;
        try
        {
            parameters = new GeneratorParams(model);
            parameters.SetSearchOption("max_length", tokens.Count + 1);
            generator = new Generator(model, parameters);
            generator.SetActiveAdapter(adapters, adapterName);
            generator.AppendTokens(tokens.ToArray());
            logits = generator.GetOutput("logits");
            if (logits.Type() != ElementType.float16 || logits.NumElements() == 0)
                throw new InvalidDataException("Cross-model recovery logits are invalid.");
            long[] shape = logits.Shape();
            float[] values = logits.GetData<Half>().ToArray()
                .Select(static value => (float)value).ToArray();
            return new CrossModelObservation(
                shape,
                values,
                values.All(float.IsFinite));
        }
        finally
        {
            TryDispose(logits, $"{stage}_logits_dispose", cleanupFailures);
            TryDispose(generator, $"{stage}_generator_dispose", cleanupFailures);
            TryDispose(parameters, $"{stage}_params_dispose", cleanupFailures);
        }
    }

    private static double MaximumAbsoluteDelta(
        CrossModelObservation left,
        CrossModelObservation right)
    {
        if (!left.Shape.SequenceEqual(right.Shape)
            || left.Values.Length != right.Values.Length)
            return double.PositiveInfinity;
        double maximum = 0.0;
        for (int index = 0; index < left.Values.Length; index++)
            maximum = Math.Max(
                maximum,
                Math.Abs(left.Values[index] - right.Values[index]));
        return maximum;
    }

    private static Config CpuConfig(
        string modelDirectory,
        ICollection<string> cleanupFailures,
        string partialDisposeStage)
    {
        var config = new Config(modelDirectory);
        try
        {
            config.ClearProviders();
            return config;
        }
        catch
        {
            TryDispose(config, partialDisposeStage, cleanupFailures);
            throw;
        }
    }

    private static void TryDispose(
        IDisposable? disposable,
        string stage,
        ICollection<string> cleanupFailures)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception exception)
        {
            cleanupFailures.Add($"{stage}:{exception.GetType().Name}");
        }
    }

    private sealed record CrossModelObservation(
        IReadOnlyList<long> Shape,
        float[] Values,
        bool Finite);
}
