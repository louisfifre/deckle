using System.IO;
using System.Text.Json;

namespace Deckle.Autocorrect;

// One approved mistouch family, as the live engine consumes it (CONTEXT.md
// § Mistouch family). The KINDS are code — universal keyboard mechanics the
// corrector knows how to interpret; the INSTANCES are per-user data, mined
// from that user's corpus and approved through the review gate. Nothing
// user-specific is ever frozen in code: another user inherits the kinds,
// never these records.
//
// Signature is the mined identity ("sub ;→'"), echoed in telemetry and in the
// personal-dictionary suppression it writes on undo. Punctuation parameterizes
// the kinds that need one (the glued boundary of a missing-space family).
public sealed record MistouchFamilyRecord(string Signature, string Kind, string Punctuation = "");

// Closed vocabulary of the interpretable kinds — one spelling, one place.
// Growing it is a code act; growing the records is a data act.
public static class MistouchFamilyKinds
{
    /// <summary>The key beside the apostrophe hit at an elision boundary (« qu;il »).</summary>
    public const string BoundaryApostrophe = "boundary_apostrophe";

    /// <summary>The space never typed behind a punctuation gluing two words (« mot,mot »).</summary>
    public const string BoundaryMissingSpace = "boundary_missing_space";
}

// Loads the approved families from the module's user-data root — the same home
// as the personal dictionary, and the same discipline: inspectable, editable,
// removable. Tolerant by design: no file, or a file that does not parse, is an
// empty set (the corrector simply stays inert), never a boot failure.
public static class MistouchFamilyStore
{
    public const string FileName = "mistouch-families.json";

    public static IReadOnlyList<MistouchFamilyRecord> Load(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<MistouchFamilyRecord>();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<MistouchFamilyRecord>();

            var records = new List<MistouchFamilyRecord>();
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                string signature = Str(item, "signature");
                string kind = Str(item, "kind");
                if (signature.Length == 0 || kind.Length == 0)
                    continue; // an unreadable record is skipped, never fatal
                records.Add(new MistouchFamilyRecord(signature, kind, Str(item, "punctuation")));
            }
            return records;
        }
        catch (JsonException)
        {
            return Array.Empty<MistouchFamilyRecord>();
        }
    }

    private static string Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
