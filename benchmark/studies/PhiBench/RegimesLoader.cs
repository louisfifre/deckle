using Deckle.Benchmark.PhiBench.Models;
using Tomlyn;
using Tomlyn.Model;

namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Loads transcription regimes from a TOML file with the same shape as
/// <c>benchmark/prompts/transcription/voxtral_validation.toml</c> :
///
/// <code>
/// [T1_baseline]
/// label = "..."
/// prompt = ""
/// system_prompt = ""
///
/// [T2_verbatim]
/// label = "..."
/// prompt = "..."
/// ...
/// </code>
///
/// Top-level table = regime name. Each regime carries optional
/// <c>label</c> / <c>prompt</c> / <c>system_prompt</c> (any missing key
/// defaults to empty).
/// </summary>
public static class RegimesLoader
{
    public static List<Regime> Load(string tomlPath, string only = "all")
    {
        if (!File.Exists(tomlPath))
            throw new FileNotFoundException($"regimes file not found: {tomlPath}", tomlPath);

        var doc = Toml.ToModel(File.ReadAllText(tomlPath));
        var result = new List<Regime>();
        var wanted = ParseFilter(only);

        foreach (var pair in doc)
        {
            if (pair.Value is not TomlTable table) continue;
            if (wanted != null && !wanted.Contains(pair.Key)) continue;

            result.Add(new Regime(
                Name: pair.Key,
                Label: table.TryGetValue("label", out var label) ? label?.ToString() ?? pair.Key : pair.Key,
                Prompt: table.TryGetValue("prompt", out var prompt) ? prompt?.ToString() ?? string.Empty : string.Empty,
                SystemPrompt: table.TryGetValue("system_prompt", out var sp) ? sp?.ToString() ?? string.Empty : string.Empty));
        }
        return result;
    }

    private static HashSet<string>? ParseFilter(string only)
    {
        if (string.IsNullOrWhiteSpace(only) || only == "all") return null;
        return new HashSet<string>(only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
