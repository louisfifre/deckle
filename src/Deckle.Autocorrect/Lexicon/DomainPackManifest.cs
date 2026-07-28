using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Deckle.Autocorrect;

// ── DomainPackManifest ──────────────────────────────────────────────────────
//
// The dilution indicator of one pack, as fabrication measured it: what the pack
// brings and what was refused to protect the base lexicon's own corrections
// (CONTEXT.md § Pack sanitization). Sanitization happens at build, so these
// numbers can only come from there — this is the machine-readable side of the
// fabrication report, written by DomainPackBuilder next to the pack artifact and
// shipped with it. The report stays the human record, with the per-form tables
// and the judge's reasoning; the manifest is the handful of totals the settings
// page reads. Both are written in the same pass from the same counts.
//
// Read on the settings page, never on the correction path. A pack whose
// manifest is missing or malformed simply shows no figures — the indicator is
// informative, and a build without it must still correct.
public sealed record DomainPackManifest
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    // The pack this describes, matching DomainPack.Id ("fr-it").
    public string Id { get; init; } = string.Empty;

    // Forms the shipped artifact carries — what the pack brings.
    public int ShippedForms { get; init; }

    // Of those, the ones the frequency overlay lifted off the flat floor
    // because they are genuinely common (the IT bench's promotion call).
    public int PromotedForms { get; init; }

    // Candidates whose masking cost sat above the exclusion threshold: keeping
    // them would have smothered a base-lexicon correction outright.
    public int RefusedAboveThreshold { get; init; }

    // Gray-zone candidates the external judge ruled out — Wiktionary noise,
    // never-typed coinages, French misspellings that must stay correctable.
    public int RefusedByJudge { get; init; }

    // Gray-zone candidates still awaiting a verdict. Withheld from the pack
    // until judged, so a non-zero count means the pack is not finished.
    public int PendingJudgment { get; init; }

    // Candidates dropped before sanitization because the base lexicon already
    // had them — overlap, not refusal, and not part of the dilution figure.
    public int AlreadyInBaseLexicon { get; init; }

    // What the pack cost to stay safe: everything sanitization turned away.
    // Derived, never stored — the file carries measured counts only, so a
    // reader can never find a total that disagrees with its parts.
    [JsonIgnore]
    public int RefusedForms => RefusedAboveThreshold + RefusedByJudge;

    public static DomainPackManifest? TryLoad(string dataDir, DomainPack pack)
    {
        string path = Path.Combine(dataDir, pack.ManifestFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<DomainPackManifest>(
                File.ReadAllText(path), _jsonOptions);
        }
        catch (JsonException)
        {
            // A corrupt manifest costs the figures, never the pack.
            return null;
        }
    }

    public void Write(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
}
