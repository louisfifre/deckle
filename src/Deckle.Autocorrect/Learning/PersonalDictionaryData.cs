namespace Deckle.Autocorrect;

// On-disk shape of the personal dictionary — the only persisted text in the
// module (CLAUDE.md), serialized indented so the file is humanly inspectable.
public sealed class PersonalDictionaryData
{
    // Schema 2 replaces the old decaying weight with the Phase 2 adoption
    // contract: clean recurrence across distinct days plus a protection category.
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; }
    public List<WordEntry> Words { get; set; } = new();
    public List<SuppressionEntry> Suppressions { get; set; } = new();
}

// The protection semantics an adopted word gets. Anglicism and Other are
// case-insensitive; ProperNoun is case-sensitive, because the capital is part of
// the protected form.
public enum PersonalWordCategory
{
    Other,
    Anglicism,
    ProperNoun,
}

// A word whose clean typed occurrences may earn adoption. CleanOccurrences counts
// only occurrences still considered clean; CleanDays stores the per-day counts so
// a same-day re-edit can remove exactly the latest occurrence without destroying a
// real recurrence on another day.
public sealed class WordEntry
{
    public string Word { get; set; } = string.Empty;
    public PersonalWordCategory Category { get; set; } = PersonalWordCategory.Other;
    public int CleanOccurrences { get; set; }
    public Dictionary<string, int> CleanDays { get; set; } = new(StringComparer.Ordinal);
    public int DirtyOccurrences { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

// A suppressed correction: (Original -> Replacement) must never fire on its own
// again (CLAUDE.md). Explicit, persisted, removable by the user.
public sealed class SuppressionEntry
{
    public string Original { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
