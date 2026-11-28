using System.Globalization;
using System.Net;
using System.Reflection;

namespace Deckle.Browser;

// The registrable domain of a host (the public suffix plus one label, "eTLD+1"),
// from the Public Suffix List (ICANN and private sections, wildcard and
// exception rules). It is the part of an address a person can trust to say who
// runs the site: "mail.example.co.uk" is "example.co.uk".
public static class RegistrableDomain
{
    private static readonly Lazy<Rules> List = new(Load);

    private sealed record Rules(HashSet<string> Plain, HashSet<string> Wildcard, HashSet<string> Exceptions);

    /// <summary>The registrable domain of <paramref name="host"/>, in its ASCII
    /// (punycode) form. An IP literal, "localhost", a single label or a host
    /// that is itself a public suffix comes back as it is.</summary>
    public static string Of(string host) => Of(host, List.Value);

    private static string Of(string host, Rules rules)
    {
        string name = Normalize(host);
        if (name.Length == 0 || name == "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal)) return name;
        if (IPAddress.TryParse(name, out _)) return name;

        string[] labels = name.Split('.');
        if (labels.Length < 2) return name;

        int suffixLength = SuffixLength(labels, rules);
        if (labels.Length <= suffixLength) return name;
        return string.Join('.', labels[(labels.Length - suffixLength - 1)..]);
    }

    // Lower case, no trailing dot, no brackets, ASCII form of the labels.
    internal static string Normalize(string host)
    {
        string name = host.Trim().TrimEnd('.').Trim('[', ']').ToLowerInvariant();
        if (name.Length == 0) return "";
        if (name.All(char.IsAscii)) return name;
        try { return new IdnMapping().GetAscii(name).ToLowerInvariant(); }
        catch (ArgumentException) { return name; }
    }

    // The number of labels of the public suffix the rules give this host: an
    // exception rule wins, then the longest matching rule, else one label.
    private static int SuffixLength(string[] labels, Rules rules)
    {
        int best = 1;
        for (int i = 0; i < labels.Length; i++)
        {
            string candidate = string.Join('.', labels[i..]);
            if (rules.Exceptions.Contains(candidate)) return labels.Length - i - 1;

            int count = labels.Length - i;
            bool matches = rules.Plain.Contains(candidate)
                || (count > 1 && rules.Wildcard.Contains(string.Join('.', labels[(i + 1)..])));
            if (matches && count > best) best = count;
        }
        return best;
    }

    private static Rules Load()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Deckle.Browser.public_suffix_list.dat")
            ?? throw new InvalidOperationException("The public suffix list is not embedded.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static Rules Parse(string text)
    {
        var rules = new Rules([], [], []);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            // A rule is the first word of a line; "//" opens a comment.
            int space = line.IndexOfAny([' ', '\t']);
            if (space >= 0) line = line[..space];
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;

            if (line[0] == '!') rules.Exceptions.Add(Normalize(line[1..]));
            else if (line.StartsWith("*.", StringComparison.Ordinal)) rules.Wildcard.Add(Normalize(line[2..]));
            else rules.Plain.Add(Normalize(line));
        }
        return rules;
    }
}
