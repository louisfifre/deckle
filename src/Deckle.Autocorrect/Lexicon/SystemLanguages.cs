using System.Globalization;

namespace Deckle.Autocorrect;

// ── SystemLanguages ─────────────────────────────────────────────────────────
//
// The languages Windows says the user reads and writes — the source of a
// language row's default on the Lexical domains page. A domain the user writes
// in, in a language they already declared to the system, is on without being
// asked; anything else waits to be turned on. The stored toggle always wins
// over this (DomainActivation), so detection informs the default and never
// overrides a choice.
//
// Read through GlobalizationPreferences, the user's own language list rather
// than the UI language — a French user running Windows in English still writes
// French. Reduced to primary subtags ("fr-FR" -> "fr") because a pack extends a
// language, not a regional variant. No capability to declare: the list is
// user-profile data every desktop app may read.
//
// Cached for the process. The list is stable within a session in practice, the
// effective-lexicon key must not wobble under a running engine, and Windows
// exposes no change signal for it — so a language added in Windows Settings
// takes effect at the next Deckle launch rather than being polled for.
public static class SystemLanguages
{
    private static readonly Lazy<IReadOnlySet<string>> _current = new(Detect);

    public static IReadOnlySet<string> Current => _current.Value;

    private static IReadOnlySet<string> Detect()
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string tag in Windows.System.UserProfile.GlobalizationPreferences.Languages)
                Add(languages, tag);
        }
        catch (Exception)
        {
            // The WinRT projection is unavailable (a test host, a service
            // context). The culture of the running user is the honest fallback:
            // narrower than the real list, never wider, so the worst case is a
            // pack the user turns on by hand.
            languages.Clear();
        }

        if (languages.Count == 0)
        {
            Add(languages, CultureInfo.CurrentUICulture.Name);
            Add(languages, CultureInfo.InstalledUICulture.Name);
        }
        return languages;
    }

    private static void Add(HashSet<string> languages, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        int dash = tag.IndexOf('-');
        string primary = dash < 0 ? tag : tag[..dash];
        if (primary.Length > 0) languages.Add(primary);
    }
}
