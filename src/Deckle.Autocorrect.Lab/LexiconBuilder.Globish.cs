using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Lab;

public static partial class LexiconBuilder
{
    // ── Restricted English seed: FranceTerme foreign equivalents ───────────
    //
    // FranceTerme is an official terminology base. The French heads are NOT the
    // seed; only English foreign equivalents are considered. The live guard is
    // per-token, so multi-word equivalents are reduced to conservative ASCII
    // content tokens, then filtered against French exact/accent-fold forms and
    // one-edit French neighbours. This is deliberately narrower than a general
    // English frequency list: it protects technical globish without turning the
    // historical full English lexicon back on.
    private static void BuildGlobalEnglishSeed(
        string sourcePath, string outPath, FrequencyLexicon french)
    {
        if (!File.Exists(sourcePath))
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
            Console.WriteLine("Globish: FranceTerme.xml absent — restricted English seed not built.");
            return;
        }


        var (frenchForms, frenchDeletionKeys) = BuildFrenchCollisionIndex(french);
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        long equivalentValues = 0;
        long skippedShape = 0;
        long skippedFrenchCollision = 0;

        var doc = XDocument.Load(sourcePath, LoadOptions.None);
        foreach (XElement equivalent in doc.Descendants("Equivalent"))
        {
            if (!string.Equals(
                    (string?)equivalent.Attribute("langue"),
                    "en",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (XElement prop in equivalent.Elements("Equi_prop"))
            {
                equivalentValues++;
                var seenInEquivalent = new HashSet<string>(StringComparer.Ordinal);
                foreach (string chunk in GlobishChunks(prop.Value))
                {
                    if (!TryNormalizeGlobishToken(chunk, out string token))
                    {
                        skippedShape++;
                        continue;
                    }
                    if (!seenInEquivalent.Add(token))
                        continue;
                    if (IsFrenchCollision(token, frenchForms, frenchDeletionKeys))
                    {
                        skippedFrenchCollision++;
                        continue;
                    }

                    counts[token] = counts.TryGetValue(token, out double prior) ? prior + 1.0 : 1.0;
                }
            }
        }

        WriteLexicon(outPath, counts);
        Console.WriteLine($"Globish: kept {counts.Count:N0} FranceTerme English tokens "
                        + $"from {equivalentValues:N0} English equivalents "
                        + $"({skippedFrenchCollision:N0} French collisions, "
                        + $"{skippedShape:N0} shape/stopword rejects).");
    }

    private static (HashSet<string> Forms, HashSet<string> DeletionKeys) BuildFrenchCollisionIndex(
        FrequencyLexicon french)
    {
        var forms = new HashSet<string>(StringComparer.Ordinal);
        var deletionKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (form, _) in french.Entries)
        {
            AddFrenchCollisionForm(AccentFolding.Fold(form), forms, deletionKeys);
        }

        return (forms, deletionKeys);
    }

    private static void AddFrenchCollisionForm(
        string form, HashSet<string> forms, HashSet<string> deletionKeys)
    {
        foreach (string chunk in GlobishChunks(form))
        {
            if (!TryNormalizeGlobishToken(chunk, out string token))
                continue;
            if (forms.Add(token))
                AddDeletionKeys(token, deletionKeys);
        }
    }

    private static bool IsFrenchCollision(
        string token, HashSet<string> frenchForms, HashSet<string> frenchDeletionKeys)
    {
        if (frenchForms.Contains(token) || frenchDeletionKeys.Contains(token))
            return true;

        if (token.Length <= 3)
            return false;

        for (int i = 0; i < token.Length; i++)
        {
            string deleted = DeleteAt(token, i);
            if (frenchForms.Contains(deleted) || frenchDeletionKeys.Contains(deleted))
                return true;
        }
        return false;
    }

    private static void AddDeletionKeys(string token, HashSet<string> deletionKeys)
    {
        if (token.Length <= 3)
            return;
        for (int i = 0; i < token.Length; i++)
            deletionKeys.Add(DeleteAt(token, i));
    }

    private static string DeleteAt(string value, int index) =>
        value.Remove(index, 1);

    private static IEnumerable<string> GlobishChunks(string text)
    {
        var chunk = new StringBuilder();
        foreach (char raw in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            char c = NormalizeGlobishChar(raw);
            if (c is >= 'a' and <= 'z' || c == '-')
            {
                chunk.Append(c);
                continue;
            }

            if (chunk.Length > 0)
            {
                yield return chunk.ToString();
                chunk.Clear();
            }
        }

        if (chunk.Length > 0)
            yield return chunk.ToString();
    }

    private static char NormalizeGlobishChar(char c) => c switch
    {
        '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
        _ => c,
    };

    private static bool TryNormalizeGlobishToken(string chunk, out string token)
    {
        token = chunk.Trim('-');
        return IsGlobishTokenShape(token) && !GlobishStopWords.Contains(token);
    }

    private static bool IsGlobishTokenShape(string token)
    {
        if (token.Length < 3)
            return false;

        bool previousHyphen = false;
        foreach (char c in token)
        {
            if (c == '-')
            {
                if (previousHyphen)
                    return false;
                previousHyphen = true;
                continue;
            }
            if (c is < 'a' or > 'z')
                return false;
            previousHyphen = false;
        }

        return token[0] != '-' && token[^1] != '-';
    }
}
