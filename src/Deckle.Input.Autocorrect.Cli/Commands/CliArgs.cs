using System.Globalization;

namespace Deckle.Input.Autocorrect.Cli.Commands;

// Minimal option parser for the command tail: `--flag` (presence) and
// `--opt value` (paired). No third-party parser by doctrine; the surface is
// tiny and fixed per command. Positional args are whatever is left over.
internal sealed class CliArgs
{
    private readonly Dictionary<string, string?> _opts = new(StringComparer.Ordinal);
    private readonly List<string> _positional = new();

    // Builds from the args following the command name. A token starting with
    // "--" is an option; the next token is its value unless it is itself an
    // option (then the flag is value-less). Everything else is positional.
    public CliArgs(string[] args, int start)
    {
        for (int i = start; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                string? value = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
                _opts[a] = value;
            }
            else
            {
                _positional.Add(a);
            }
        }
    }

    public IReadOnlyList<string> Positional => _positional;

    public bool Has(string flag) => _opts.ContainsKey(flag);

    public string? Value(string opt) => _opts.TryGetValue(opt, out var v) ? v : null;

    public string ValueOr(string opt, string fallback) => Value(opt) ?? fallback;

    public double DoubleOr(string opt, double fallback) =>
        Value(opt) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : fallback;

    public int IntOr(string opt, int fallback) =>
        Value(opt) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n : fallback;
}
