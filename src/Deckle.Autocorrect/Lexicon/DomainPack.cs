using System.IO;

namespace Deckle.Autocorrect;

// ── DomainPack ──────────────────────────────────────────────────────────────
//
// One activatable set of surface forms that fully extends its language's
// lexicon — its forms become valid forms AND correction targets, exactly like
// the base lexicon's (CONTEXT.md § Lexicon composition). This is the runtime
// half: what the app ships, how its artifacts are named, and whether the user
// turned it on. Fabrication — the categories mined, the masking-cost
// sanitization, the judge campaign — lives in Deckle.Autocorrect.Lab and never
// runs here.
//
// The id carries the language it extends ("fr-it" = the computing pack under
// French), so the settings key stays unambiguous when a second language
// arrives. Artifact names derive from it by convention, so a pack added to
// Shipped needs no per-pack path anywhere.
//
// Packs are few by principle: stacking them dilutes correction coverage, which
// is why every pack ships its dilution manifest beside its forms and why
// activation is the user's deliberate act rather than a default.
public sealed record DomainPack(string Id, string ResourceKey)
{
    public string FileName => $"pack-{Id}.tsv.gz";

    // Every pack the build ships. The list is code, not data: an artifact
    // dropped into Data/ without an entry here is inert, which is the point —
    // a pack reaches the user through a reviewed release, never a stray file.
    public static IReadOnlyList<DomainPack> Shipped { get; } =
    [
        new DomainPack("fr-it", "AutocorrectPage_PackFrIt"),
    ];

    // The packs the user has turned on, in Shipped order — so the effective
    // lexicon's composition order is fixed even though the merge makes it
    // irrelevant.
    public static IReadOnlyList<DomainPack> ActiveIn(AutocorrectSettings settings)
    {
        List<DomainPack>? active = null;
        foreach (DomainPack pack in Shipped)
            if (settings.IsDomainPackActive(pack.Id))
                (active ??= new List<DomainPack>()).Add(pack);
        return active ?? (IReadOnlyList<DomainPack>)Array.Empty<DomainPack>();
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
