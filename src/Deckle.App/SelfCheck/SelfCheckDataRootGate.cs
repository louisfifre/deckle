using Deckle.Core;

namespace Deckle.App.SelfCheck;

// The answer of the gate: whether the self-check may run, and when it may not,
// the sentence the report carries.
internal readonly record struct SelfCheckGateResult(bool Allowed, string Reason);

// The one rule that keeps the self-check away from the maintainer's data: it runs
// only on a data root of its own. AppPaths freezes its root at first touch, so
// this gate decides from the two strings alone and never touches AppPaths: a
// refused run must leave no folder, no migration, no marker in the real root.
//
// Three roots are refused:
//   - none set (the app would then use the default root, the maintainer's);
//   - the default root itself, spelled in any way that resolves to it;
//   - a folder inside the default root (a "temp" subfolder there is still under
//     the folders the resident app, the relocation and the cleanups own).
internal static class SelfCheckDataRootGate
{
    public static SelfCheckGateResult Evaluate(string? dataRootVariable, string defaultRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRootVariable))
            return new(false, $"{AppPaths.DataRootEnvVar} is not set: the self-check would run on the default data root.");

        string root, normalDefault;
        try
        {
            root = Normalize(dataRootVariable);
            normalDefault = Normalize(defaultRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, $"{AppPaths.DataRootEnvVar} is not a usable path: {ex.Message}");
        }

        if (string.Equals(root, normalDefault, StringComparison.OrdinalIgnoreCase))
            return new(false, $"{AppPaths.DataRootEnvVar} is the default data root: the self-check never runs on it.");

        if (root.StartsWith(normalDefault + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return new(false, $"{AppPaths.DataRootEnvVar} is inside the default data root: the self-check needs a root of its own.");

        return new(true, "");
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
