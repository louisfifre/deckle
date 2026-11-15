namespace Deckle.Projects;

// One place a version was found: the Git tag, labelled "Git tag", or a
// manifest, labelled with its path relative to the project ("/" between
// segments). Value is written without the leading "v".
public sealed record VersionSource(string Label, string Value)
{
    public const string GitTagLabel = "Git tag";
}

// Every version Deckle found for a project: the highest Git tag and the
// versions the standard manifests declare (spec, Project page, Readouts).
//
// Sources lists what is shown, the Git tag first, then each manifest by path.
// MoreSources counts the manifests left out by the cap (a repository with many
// package.json files gives more lines than are useful), so the flyout can say
// "and N more".
public sealed record VersionReading(string? Tag, IReadOnlyList<VersionSource> Sources, int MoreSources = 0)
{
    public static VersionReading None { get; } = new(null, [], 0);

    // The number of sources, the tag included: "3 versions found" is one tag
    // and two manifests.
    public int Count => Sources.Count + MoreSources;

    // Two or more distinct values: "v0.32.0" and "0.32.0" are one, which the
    // sources already hold without their "v".
    public bool HasDifferingVersions => Sources.Select(s => s.Value).Distinct(StringComparer.Ordinal).Count() > 1;

    // The card says "N versions found" only when the versions differ.
    public bool ShowsCount => HasDifferingVersions;

    // The info button beside the version: when the versions differ, and when
    // there is no tag and the manifests say something (the value still reads
    // "No version yet", the flyout lists what the manifests declare).
    public bool ShowsDetail => HasDifferingVersions || (Tag is null && Sources.Count > 0);
}
