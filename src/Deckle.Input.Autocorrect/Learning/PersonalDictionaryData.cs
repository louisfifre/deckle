namespace Deckle.Input.Autocorrect.Learning;

// On-disk shape of the personal dictionary — the only persisted text in the
// module (CLAUDE.md), serialized indented so the file is humanly inspectable.
// Words and suppression members are stored lowercase; accents are preserved.
public sealed class PersonalDictionaryData
{
    public List<WordEntry> Words { get; set; } = new();
    public List<SuppressionEntry> Suppressions { get; set; } = new();
}

// A word the user's typing has reinforced. Weight is the stored reinforcement;
// the *effective* weight at read time decays from LastSeenUtc (see
// PersonalDictionary). LastSeenUtc anchors that decay.
public sealed class WordEntry
{
    public string Word { get; set; } = string.Empty;
    public double Weight { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

// A correction the user reverted: (Original → Replacement) must never fire on
// its own again (CLAUDE.md). Explicit, persisted, removable by the user.
public sealed class SuppressionEntry
{
    public string Original { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
