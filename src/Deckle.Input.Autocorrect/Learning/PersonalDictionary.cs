using System.Text.Json;
using Deckle.Core;

namespace Deckle.Input.Autocorrect;

// The persisted learning surface: which words the user has made "theirs" and
// which corrections they reverted. Reinforcement decays so a word typed once
// and never again fades back out (the AOSP non-reinforcement oblivion); a
// revert both suppresses the correction forever and adopts the literal at once.
//
// Persistence is the shared JsonSettingsStore (debounced, atomic, mutex). This
// class owns the POCO-graph thread-safety the store delegates to the caller:
// every read and mutation runs under _lock, then Save() debounces the write.
public sealed class PersonalDictionary : IPersonalLexicon, IDisposable
{
    // Calibration (JOURNAL 2026-06-12): adoption at effective weight ≥ 3.0
    // (3-4 reinforcements in practice — decay runs between occurrences), 14-day
    // half-life, 5 000-entry cap. An entry decayed below 0.05 is dust — dropped
    // opportunistically on the next mutation.
    private const double AdoptionThreshold = 3.0;
    private const double HalfLifeDays      = 14.0;
    private const double DustThreshold     = 0.05;
    internal const int   MaxWords          = 5000;

    // Signal boosts: commit is weak repeated evidence; a hand-fixed accent is
    // stronger ("the user went back for it"); a revert is instant adoption
    // ("my word") — one revert clears the threshold on its own. The revert
    // boost sits ABOVE the threshold: equal to it, the very next decayed read
    // would already fall short; 3.5 keeps a fresh revert adopted for ~3 days
    // without reinforcement.
    private const double CommitBoost      = 1.0;
    private const double ManualAccentBoost = 1.5;
    private const double RevertBoost      = 3.5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,                              // user-inspectable surface (doctrine)
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly JsonSettingsStore<PersonalDictionaryData> _store;

    public PersonalDictionary(string filePath, Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _store = new JsonSettingsStore<PersonalDictionaryData>(
            path:        filePath,
            mutexName:   "Deckle-Autocorrect-PersonalDictionary",
            jsonOptions: JsonOptions);
    }

    private PersonalDictionaryData Data => _store.Current;

    // ── Decay ────────────────────────────────────────────────────────────────

    private double Effective(WordEntry e, DateTimeOffset now)
    {
        double days = (now - e.LastSeenUtc).TotalDays;
        if (days <= 0) return e.Weight;                    // future/clock-skew: no decay
        return e.Weight * Math.Pow(2.0, -days / HalfLifeDays);
    }

    // ── Engine-facing reads (IPersonalLexicon) ────────────────────────────────

    public bool IsAdopted(string word)
    {
        string key = Normalize(word);
        lock (_lock)
        {
            var e = Find(key);
            return e is not null && Effective(e, _clock()) >= AdoptionThreshold;
        }
    }

    public bool IsSuppressed(string original, string replacement)
    {
        string o = Normalize(original);
        string r = Normalize(replacement);
        lock (_lock)
            return Data.Suppressions.Any(s => s.Original == o && s.Replacement == r);
    }

    public IReadOnlyCollection<string> AdoptedWords => AdoptedSnapshot();

    private IReadOnlyCollection<string> AdoptedSnapshot()
    {
        var now = _clock();
        lock (_lock)
            return Data.Words
                .Where(e => Effective(e, now) >= AdoptionThreshold)
                .Select(e => e.Word)
                .ToList();
    }

    // ── Learning signals ──────────────────────────────────────────────────────

    public void RecordCommit(string word) => Reinforce(word, CommitBoost);

    // The user retyped the accented form by hand after the typo — strong
    // evidence the accented form is the one they want.
    public void RecordManualAccentFix(string typed, string fixedForm)
        => Reinforce(fixedForm, ManualAccentBoost);

    // The user undid a correction: suppress that pair forever (idempotent) and
    // adopt the literal immediately (one strong boost clears the threshold).
    public void RecordRevert(string original, string replacement)
    {
        string o = Normalize(original);
        string r = Normalize(replacement);
        var now = _clock();
        lock (_lock)
        {
            if (!Data.Suppressions.Any(s => s.Original == o && s.Replacement == r))
                Data.Suppressions.Add(new SuppressionEntry { Original = o, Replacement = r, CreatedUtc = now });
            ReinforceLocked(o, RevertBoost, now);
            PruneLocked(now);
        }
        _store.Save();
    }

    private void Reinforce(string word, double boost)
    {
        string key = Normalize(word);
        var now = _clock();
        lock (_lock)
        {
            ReinforceLocked(key, boost, now);
            PruneLocked(now);
        }
        _store.Save();
    }

    // Sets Weight to the *current effective* value plus the boost, and re-anchors
    // LastSeenUtc to now — so the decay clock restarts from each reinforcement.
    private void ReinforceLocked(string key, double boost, DateTimeOffset now)
    {
        var e = Find(key);
        if (e is null)
        {
            e = new WordEntry { Word = key };
            Data.Words.Add(e);
        }
        e.Weight = Effective(e, now) + boost;
        e.LastSeenUtc = now;
    }

    // ── Maintenance (CLI) ──────────────────────────────────────────────────────

    public IReadOnlyList<(string Word, double EffectiveWeight, bool Adopted)> SnapshotWords()
    {
        var now = _clock();
        lock (_lock)
            return Data.Words
                .Select(e =>
                {
                    double eff = Effective(e, now);
                    return (e.Word, eff, eff >= AdoptionThreshold);
                })
                .ToList();
    }

    public IReadOnlyList<(string Original, string Replacement)> SnapshotSuppressions()
    {
        lock (_lock)
            return Data.Suppressions.Select(s => (s.Original, s.Replacement)).ToList();
    }

    public bool RemoveWord(string word)
    {
        string key = Normalize(word);
        bool removed;
        lock (_lock) removed = Data.Words.RemoveAll(e => e.Word == key) > 0;
        if (removed) _store.Save();
        return removed;
    }

    public bool RemoveSuppression(string original, string replacement)
    {
        string o = Normalize(original);
        string r = Normalize(replacement);
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

    private WordEntry? Find(string key) => Data.Words.FirstOrDefault(e => e.Word == key);

    // Drop dust, then enforce the cap by evicting the lowest effective weights.
    // Run after every word mutation so the file never grows unbounded.
    private void PruneLocked(DateTimeOffset now)
    {
        Data.Words.RemoveAll(e => Effective(e, now) < DustThreshold);

        int excess = Data.Words.Count - MaxWords;
        if (excess <= 0) return;

        // Keep the heaviest: order ascending by effective weight, drop the front.
        var doomed = Data.Words
            .OrderBy(e => Effective(e, now))
            .Take(excess)
            .ToList();
        foreach (var e in doomed) Data.Words.Remove(e);
    }

    // Personal-dictionary keys are lowercase (accents preserved). Invariant
    // lowering so casing of the typed surface never splits an entry in two.
    private static string Normalize(string word) =>
        (word ?? string.Empty).ToLowerInvariant();
}
