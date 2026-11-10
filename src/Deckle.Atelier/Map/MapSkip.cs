namespace Deckle.Atelier;

// Why a part of the folder was not looked at, so the view can say it.
public enum MapSkipReason
{
    // .git and its kin: the history of the files, not things to use.
    VersionControl,

    // node_modules and virtual environments: other people's packages.
    Dependencies,

    // bin, obj, artifacts and the like: what a build writes and rewrites.
    BuildOutput,

    // A junction or a symbolic link: followed, it could loop or leave the folder.
    LinkedFolder,

    // A folder or a file the account cannot open, or a configuration file
    // that is not valid JSON.
    Unreadable,

    // A file over MapLimits.MaxFileBytes: listed, but neither read nor counted.
    TooLarge,

    // The walk stopped at MapLimits.MaxEntries; what lies beyond is not seen.
    EntryLimit,
}

public sealed record MapSkip(string RelativePath, MapSkipReason Reason);

// Bounds that keep one map from taking the machine on a huge folder.
// MaxFileBytes follows Claude Code, which skips a CLAUDE.md over 4 MiB and
// loads none of it (https://code.claude.com/docs/en/memory).
public sealed record MapLimits(int MaxEntries = 200_000, long MaxFileBytes = 4L * 1024 * 1024)
{
    public static MapLimits Default { get; } = new();
}
