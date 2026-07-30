using System.Text;

namespace Deckle.Autocorrect.Probe;

internal static class QwenAdapterCompatibilityCommand
{
    public static int Run(ProbeArguments arguments)
    {
        (QwenAdapterCompatibilityPlan? plan, QwenAdapterPlanVerdict verdict) =
            QwenAdapterCompatibilityPlanReader.TryRead(arguments.PlanPath);
        if (plan is null)
        {
            Console.Error.WriteLine($"ACX-0023 plan refused: {verdict.Code}");
            return 2;
        }

        if (!Directory.Exists(plan.ModelDirectory)
            || File.Exists(plan.OutputPath)
            || !Directory.Exists(Path.GetDirectoryName(plan.OutputPath)))
        {
            Console.Error.WriteLine("ACX-0023 paths do not satisfy the frozen run contract.");
            return 2;
        }

        QwenAdapterArtifactVerdict artifactVerdict =
            QwenAdapterArtifactGate.Evaluate(plan);
        if (!artifactVerdict.Accepted)
        {
            Console.Error.WriteLine(
                $"ACX-0023 artifact identity refused: {artifactVerdict.Code}");
            return 2;
        }

        foreach (QwenAdapterArtifactPlan adapter in plan.Adapters)
        {
            if (!File.Exists(adapter.Path) || !File.Exists(adapter.ManifestPath))
            {
                Console.Error.WriteLine($"Missing frozen adapter artifact: {adapter.Name}");
                return 2;
            }
        }

        QwenAdapterRuntimeNegativePlan missingFile = plan.RuntimeNegatives[0];
        if (File.Exists(missingFile.Path)
            || plan.RuntimeNegatives.Skip(1).Any(static negative => !File.Exists(negative.Path)))
        {
            Console.Error.WriteLine("Runtime-negative artifacts do not match their frozen path states.");
            return 2;
        }

        IReadOnlyDictionary<string, QwenAdapterManifest> manifests;
        try
        {
            manifests = plan.Adapters.ToDictionary(
                static adapter => adapter.Name,
                static adapter => QwenAdapterCompatibilityPlanReader.ReadManifest(
                    adapter.ManifestPath),
                StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or System.Text.Json.JsonException or NotSupportedException)
        {
            Console.Error.WriteLine(
                $"ACX-0023 adapter manifest could not be read: {exception.GetType().Name}");
            return 2;
        }

        QwenAdapterCompatibilityReport report;
        try
        {
            var runner = new QwenAdapterCompatibilityRunner(
                new OnnxQwenAdapterProbeRuntimeFactory());
            report = runner.Run(plan, manifests);
        }
        catch (Exception exception)
        {
            report = QwenAdapterCompatibilityRunner.FatalReport(
                plan,
                exception,
                "command_runtime");
        }

        string json = QwenAdapterCompatibilityPlanReader.SerializeReport(report);
        using (var stream = new FileStream(
            plan.OutputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            writer.Write(json);

        Console.WriteLine($"Experiment : {report.ExperimentId} phase {report.Phase}");
        Console.WriteLine($"Provider   : {report.Provider}");
        Console.WriteLine($"Verdict    : {report.Verdict}");
        Console.WriteLine($"Output     : {plan.OutputPath}");
        Console.WriteLine($"Claim      : {report.ClaimBoundary}");
        return report.Valid ? 0 : 1;
    }
}
