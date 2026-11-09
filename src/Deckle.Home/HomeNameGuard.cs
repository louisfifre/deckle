using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Deckle.Home;

// The near-name guard of a typed create. A free-titled object has no code to
// tell it apart, so a name dictated twice ("Visseuse", "visseuses") would
// silently make a second object; the guard refuses it with the objects it
// resembles, and the caller either updates the one that exists or passes
// force for a true twin. Coded types keep their code as identity: two sockets
// named alike in two rooms are normal, so they never come here.
//
// Two names are near when their folded forms are equal: case, diacritics,
// ligatures, punctuation and spacing do not count, nor a plural s or x at the
// end of a word. Equality of folded forms, never a distance, so the guard
// refuses only what a person would read as the same name.
internal sealed class HomeNameGuard
{
    private readonly string _type;
    private readonly List<(string Folded, string Candidate)> _known = [];
    private readonly List<(string Display, string[] Candidates)> _refusals = [];

    public HomeNameGuard(string type, HomeObjectIndex index)
    {
        _type = type;
        foreach (JsonObject value in index.Objects.Where(value => HomeObjectJson.TypeKey(value) == type))
        {
            string folded = Fold(ComparedName(value));
            if (folded.Length > 0)
                _known.Add((folded, $"{HomeObjectJson.Id(value)} · {HomeObjectIndex.Display(value)} ({type})"));
        }
    }

    public static bool Applies(string type) => !HomeSchema.CodedTypes.Contains(type, StringComparer.Ordinal);

    // Records one item of the batch: refused when its name folds like an
    // existing object or an earlier item, unless forced. Every item joins the
    // known names, so a later item of the same batch meets it too.
    public void Check(string name, string display, bool force)
    {
        string folded = Fold(name);
        if (folded.Length == 0) return;
        if (!force)
        {
            string[] candidates = _known.Where(known => known.Folded == folded)
                .Select(known => known.Candidate).ToArray();
            if (candidates.Length > 0) _refusals.Add((display, candidates));
        }
        _known.Add((folded, $"(dans ce lot) · {display} ({_type})"));
    }

    public void ThrowIfRefused()
    {
        if (_refusals.Count == 0) return;
        int candidates = _refusals.Sum(refusal => refusal.Candidates.Length);
        DeckleHomeSource.Log.NameGuardRefused(_type, candidates);

        var lines = new List<string>
        {
            "Nom trop proche d'un objet existant du même type : rien n'a été créé.",
        };
        foreach ((string display, string[] found) in _refusals)
        {
            lines.Add($"« {display} » ressemble à :");
            lines.AddRange(found.Select(candidate => "- " + candidate));
        }
        lines.Add($"Si c'est le même objet, modifie-le avec {_type}_update et son id. "
            + "Si c'est un vrai jumeau, relance l'item avec force: true.");
        throw new InvalidOperationException(string.Join("\n", lines));
    }

    // An idea has no title: it is compared by the first line of its text.
    public static string ComparedName(JsonObject value)
    {
        if (HomeObjectJson.TypeKey(value) != HomeSchema.Types.Idea) return HomeObjectJson.Name(value);
        string content = HomeObjectJson.String(value, "snippet");
        if (string.IsNullOrWhiteSpace(content)) content = HomeObjectJson.String(value, "markdown");
        return FirstLine(content);
    }

    public static string FirstLine(string? content) =>
        string.IsNullOrWhiteSpace(content) ? "" : content.Split('\n', 2)[0].Trim();

    public static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        // KD decomposes the accents and the compatibility ligatures (ﬁ);
        // œ and æ are letters of their own and need spelling out.
        string decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
                continue;
            char lower = char.ToLowerInvariant(character);
            switch (lower)
            {
                case 'œ': builder.Append("oe"); continue;
                case 'æ': builder.Append("ae"); continue;
                case 'ß': builder.Append("ss"); continue;
            }
            builder.Append(char.IsLetterOrDigit(lower) ? lower : ' ');
        }
        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Singular));
    }

    // Only a word longer than three letters loses its last s or x, so short
    // words that end that way (vis, bus) are not cut.
    private static string Singular(string word) =>
        word.Length > 3 && word[^1] is 's' or 'x' ? word[..^1] : word;
}
