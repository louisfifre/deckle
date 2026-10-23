using System;
using System.IO;

namespace Deckle.Core;

// The rule a module id must meet to name a folder under modules\. Kept apart
// from AppPaths because that class creates folders in its static constructor,
// so touching it from a test writes into the real data root.
public static class ModuleDirectoryName
{
    // True when the id is one plain path segment. Path.Combine returns a rooted
    // id as is and does not look at "..", so either would resolve outside the
    // modules folder; the invalid file-name characters cover the separators,
    // the drive colon and the wildcards.
    public static bool IsSingleSegment(string? moduleId)
        => !string.IsNullOrWhiteSpace(moduleId)
           && moduleId is not ("." or "..")
           && moduleId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
