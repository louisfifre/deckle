using System.IO;

namespace Deckle.Autocorrect;

// ── DomainPack ──────────────────────────────────────────────────────────────
//
// One activatable set of surface forms that fully extends its language's
// lexicon — its forms become valid forms AND correction targets, exactly like
// the base lexicon's (CONTEXT.md § Lexicon composition). This is the runtime
// half: what the app ships and how its artifacts are named. Fabrication — the
// categories mined, the masking-cost sanitization, the judge campaign — lives
// in Deckle.Autocorrect.Lab and never runs here.
//
// One pack IS one lexical domain in one language: the id names the pair in short
// form ("fr-it" = the computing domain under French), so the settings key stays
// unambiguous when a second language arrives, and artifact names derive from it
// by convention — a pack added to Shipped needs no per-pack path anywhere. The id
// is frozen: it is what the settings file records and what the shipped artifacts
// are named, so it never follows a rename of the two halves it abbreviates. Those
// halves are carried apart as well, because the settings surface groups by domain
// and names the row by its language: DomainId points at the LexicalDomain that
// supplies the wording, Language is a BCP-47 primary subtag ("fr") resolved to a
// display name by the OS, so no per-language .resw key exists to fall out of date.
//
// Whether the user gets a pack is not asked here: DomainActivation answers it
// from the stored choice, falling back to the Windows language list.
public sealed record DomainPack(string Id, string DomainId, string Language)
{
    public string FileName => $"pack-{Id}.tsv.gz";

    // The dilution indicator, shipped beside the forms it describes.
    public string ManifestFileName => $"pack-{Id}.manifest.json";

    // Every pack the build ships. The list is code, not data: an artifact
    // dropped into Data/ without an entry here is inert, which is the point —
    // a pack reaches the user through a reviewed release, never a stray file.
    public static IReadOnlyList<DomainPack> Shipped { get; } =
    [
        new DomainPack("fr-it", "computing", "fr"),
    ];

    // The packs of one domain, in Shipped order — the language rows the
    // settings page lists under a domain tab. A domain no build ships a pack
    // for answers empty rather than throwing: the domain list is wording, the
    // pack list is artifacts, and the two are allowed to disagree.
    public static IReadOnlyList<DomainPack> InDomain(string domainId)
    {
        List<DomainPack>? packs = null;
        foreach (DomainPack pack in Shipped)
            if (string.Equals(pack.DomainId, domainId, StringComparison.Ordinal))
                (packs ??= new List<DomainPack>()).Add(pack);
        return packs ?? (IReadOnlyList<DomainPack>)Array.Empty<DomainPack>();
    }

    // Reads the pack's forms from the app's Data directory. Returns null when
    // the artifact is absent: a build missing a pack file degrades to the base
    // lexicon rather than leaving autocorrect unbuilt — the pack is an
    // extension, never a prerequisite.
    public FrequencyLexicon? TryLoad(string dataDir)
    {
        string path = Path.Combine(dataDir, FileName);
        return File.Exists(path) ? FrequencyLexicon.LoadTsvGz(path) : null;
    }
}
