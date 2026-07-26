using System.Text.Json;
using System.Text.Json.Serialization;
using Deckle.Core;

namespace Deckle.Autocorrect;

// The persisted learning surface: adopted words and suppressed corrections.
// Adoption is earned by clean recurrence, not by raw weight: the word must be
// typed verbatim often enough, across distinct days, and occurrences reopened by
// the user are removed from the clean count. This keeps accidental one-session
// bursts and corrected mistakes out of the protected vocabulary.
public sealed class PersonalDictionary : IPersonalLexicon, IDisposable
{
    internal const int RequiredCleanOccurrences = 3;
    internal const int RequiredDistinctDays = 2;
    internal const int MaxWords = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, bool> _wordAdmission;
    private readonly JsonSettingsStore<PersonalDictionaryData> _store;

    public int RemovedOnLoad { get; private set; }

    public PersonalDictionary(
        string filePath,
        Func<DateTimeOffset>? clock = null,
        Func<string, bool>? wordAdmission = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _wordAdmission = wordAdmission ?? (_ => true);
        _store = new JsonSettingsStore<PersonalDictionaryData>(
            path: filePath,
            mutexName: "Deckle-Autocorrect-PersonalDictionary",
            jsonOptions: JsonOptions,
            postLoadMigration: MigrateAndRevalidate);
    }

    // Older schemas either predate the corruption purge or cannot express the new
    // clean-day contract. Do not infer: restart the learning surface cleanly.
    private bool MigrateAndRevalidate(PersonalDictionaryData data)
    {
        bool mutated = false;
        if (data.SchemaVersion < PersonalDictionaryData.CurrentSchemaVersion)
        {
            data.Words.Clear();
            data.Suppressions.Clear();
            data.SchemaVersion = PersonalDictionaryData.CurrentSchemaVersion;
            mutated = true;
        }

        // The admission policy is lexical rather than structural, so it does not
        // require a schema bump. Revalidate current files on every load and remove
        // only entries that could never be admitted now; suppressions are explicit
        // user decisions and remain untouched.
        RemovedOnLoad = data.Words.RemoveAll(e => !_wordAdmission(e.Word));
        if (RemovedOnLoad > 0)
            mutated = true;

        return mutated;
    }

    private PersonalDictionaryData Data => _store.Current;

    // ── Engine-facing reads (IPersonalLexicon) ────────────────────────────────

    public bool IsAdopted(string word)
    {
        lock (_lock)
        {
            foreach (WordEntry e in Data.Words)
                if (Matches(e, word) && IsAdoptedEntry(e))
                    return true;
            return false;
        }
    }

    public bool IsSuppressed(string original, string replacement)
    {
        string o = NormalizeInsensitive(original);
        string r = NormalizeInsensitive(replacement);
        lock (_lock)
            return Data.Suppressions.Any(s => s.Original == o && s.Replacement == r);
    }

    public IReadOnlyCollection<string> AdoptedWords => AdoptedSnapshot();

    private IReadOnlyCollection<string> AdoptedSnapshot()
    {
        lock (_lock)
            return Data.Words
                .Where(IsAdoptedEntry)
                .Select(e => e.Word)
                .ToList();
    }

    // ── Learning signals ──────────────────────────────────────────────────────

    public bool RecordCommit(string word)
    {
        string trimmed = (word ?? string.Empty).Trim();
        if (trimmed.Length == 0) return false;
        if (!_wordAdmission(trimmed)) return false;

        var category = Categorize(trimmed);
        string key = NormalizeForCategory(trimmed, category);
        var now = _clock();
        string day = DayKey(now);

        lock (_lock)
        {
            WordEntry e = Find(key, category) ?? AddEntry(key, category, now);
            e.CleanOccurrences++;
            e.CleanDays[day] = e.CleanDays.TryGetValue(day, out int count) ? count + 1 : 1;
            e.LastSeenUtc = now;
            PruneLocked();
        }
        _store.Save();
        return true;
    }

    // The user reopened a committed word and retyped it. The occurrence that just
    // fed adoption is no longer clean, so remove one clean count for the same day.
    public void RecordReEdit(string word)
    {
        string raw = (word ?? string.Empty).Trim();
        if (raw.Length == 0) return;

        var now = _clock();
        string day = DayKey(now);
        lock (_lock)
        {
            WordEntry? e = FindMatching(raw);
            if (e is null) return;

            if (e.CleanDays.TryGetValue(day, out int count) && count > 0)
            {
                if (count == 1) e.CleanDays.Remove(day);
                else e.CleanDays[day] = count - 1;
                if (e.CleanOccurrences > 0) e.CleanOccurrences--;
            }
            e.DirtyOccurrences++;
            e.LastSeenUtc = now;
        }
        _store.Save();
    }

    // Manual accent fixes are useful correction-pair evidence, but they are not a
    // clean verbatim occurrence for adoption. Keep the API for the engine signal;
    // the personal vocabulary gate deliberately ignores it.
    public void RecordManualAccentFix(string typed, string fixedForm) { }

    public void RecordSuppression(string original, string replacement)
    {
        string o = NormalizeInsensitive(original);
        string r = NormalizeInsensitive(replacement);
        lock (_lock)
        {
            if (Data.Suppressions.Any(s => s.Original == o && s.Replacement == r))
                return;
            Data.Suppressions.Add(new SuppressionEntry { Original = o, Replacement = r, CreatedUtc = _clock() });
        }
        _store.Save();
    }

    // ── Maintenance (CLI) ──────────────────────────────────────────────────────

    public IReadOnlyList<(string Word, double EffectiveWeight, bool Adopted)> SnapshotWords()
    {
        lock (_lock)
            return Data.Words
                .Select(e => (e.Word, (double)e.CleanOccurrences, IsAdoptedEntry(e)))
                .ToList();
    }

    public IReadOnlyList<(string Original, string Replacement)> SnapshotSuppressions()
    {
        lock (_lock)
            return Data.Suppressions.Select(s => (s.Original, s.Replacement)).ToList();
    }

    public bool RemoveWord(string word)
    {
        string raw = (word ?? string.Empty).Trim();
        bool removed;
        lock (_lock)
            removed = Data.Words.RemoveAll(e => Matches(e, raw)) > 0;
        if (removed) _store.Save();
        return removed;
    }

    public bool RemoveSuppression(string original, string replacement)
    {
        string o = NormalizeInsensitive(original);
        string r = NormalizeInsensitive(replacement);
        bool removed;
        lock (_lock)
            removed = Data.Suppressions.RemoveAll(s => s.Original == o && s.Replacement == r) > 0;
        if (removed) _store.Save();
        return removed;
    }

    public void Purge()
    {
        lock (_lock)
        {
            Data.Words.Clear();
            Data.Suppressions.Clear();
        }
        _store.Save();
    }

    public void Flush() => _store.Flush();

    public void Dispose() => Flush();

    // ── Internals ──────────────────────────────────────────────────────────────

    private static bool IsAdoptedEntry(WordEntry e) =>
        e.CleanOccurrences >= RequiredCleanOccurrences
        && e.CleanDays.Count >= RequiredDistinctDays;

    private WordEntry AddEntry(string key, PersonalWordCategory category, DateTimeOffset now)
    {
        var e = new WordEntry
        {
            Word = key,
            Category = category,
            FirstSeenUtc = now,
            LastSeenUtc = now,
        };
        Data.Words.Add(e);
        return e;
    }

    private WordEntry? Find(string key, PersonalWordCategory category) =>
        Data.Words.FirstOrDefault(e => e.Category == category && e.Word == key);

    private WordEntry? FindMatching(string word) =>
        Data.Words.FirstOrDefault(e => Matches(e, word));

    private static bool Matches(WordEntry e, string word)
    {
        if (e.Category == PersonalWordCategory.ProperNoun)
            return string.Equals(e.Word, word, StringComparison.Ordinal);
        return string.Equals(e.Word, NormalizeInsensitive(word), StringComparison.Ordinal);
    }

    private static PersonalWordCategory Categorize(string word)
    {
        if (WordShape.HasInternalUpper(word) || WordShape.IsTitleCase(word))
            return PersonalWordCategory.ProperNoun;
        return IsAsciiWord(word) ? PersonalWordCategory.Anglicism : PersonalWordCategory.Other;
    }

    private static string NormalizeForCategory(string word, PersonalWordCategory category) =>
        category == PersonalWordCategory.ProperNoun ? word : NormalizeInsensitive(word);

    private static string NormalizeInsensitive(string word) =>
        (word ?? string.Empty).ToLowerInvariant();

    private static string DayKey(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsAsciiWord(string word)
    {
        foreach (char c in word)
        {
            if (c is '-' or '\'') continue;
            if (c is < 'A' or > 'Z' && c is < 'a' or > 'z')
                return false;
        }
        return true;
    }

    private void PruneLocked()
    {
        int excess = Data.Words.Count - MaxWords;
        if (excess <= 0) return;

        var doomed = Data.Words
            .OrderBy(e => IsAdoptedEntry(e))
            .ThenBy(e => e.CleanOccurrences)
            .ThenBy(e => e.CleanDays.Count)
            .ThenBy(e => e.LastSeenUtc)
            .Take(excess)
            .ToList();
        foreach (var e in doomed) Data.Words.Remove(e);
    }
}
