using Deckle.Core;

namespace Deckle.Autocorrect.Probe;

internal sealed record ModelSpec(string Label, string Directory)
{
    public static ModelSpec Parse(string value)
    {
        int separator = value.IndexOf('=');
        if (separator > 0)
        {
            string label = value[..separator].Trim();
            string directory = value[(separator + 1)..].Trim();
            return new ModelSpec(
                string.IsNullOrWhiteSpace(label) ? ModelPathResolver.LabelFromPath(directory) : label,
                directory);
        }

        return new ModelSpec(ModelPathResolver.LabelFromPath(value), value);
    }
}

internal static class ModelPathResolver
{
    private static readonly string[] DefaultBenchmarkModelNames =
    {
        "qwen3-0.6b-onnx",
        "qwen3-1.7b-onnx",
        "qwen3-4b-onnx",
        "qwen3-8b-onnx",
    };

    public static ModelSpec DefaultSingleModel()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("DECKLE_ONNX_JUDGE_MODEL_DIR");
        string directory = string.IsNullOrWhiteSpace(overrideDir)
            ? DefaultModelDirectory("qwen3-0.6b-onnx")
            : overrideDir;

        return new ModelSpec(LabelFromPath(directory), directory);
    }

    public static IReadOnlyList<ModelSpec> DefaultBenchmarkModels() =>
        DefaultBenchmarkModelNames
            .Select(name => new ModelSpec(name, DefaultModelDirectory(name)))
            .Where(model => System.IO.Directory.Exists(model.Directory))
            .ToArray();

    public static string LabelFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "model";

        var directory = new DirectoryInfo(path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
        {
            if (current.Name.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Contains("luth", StringComparison.OrdinalIgnoreCase))
                return current.Name;
        }

        return directory.Name;
    }

    private static string DefaultModelDirectory(string modelName) =>
        Path.Combine(
            AppPaths.ModelsDirectory,
            modelName,
            "onnxruntime",
            "cpu_and_mobile",
            "cpu-int4-kld-block-128");
}
