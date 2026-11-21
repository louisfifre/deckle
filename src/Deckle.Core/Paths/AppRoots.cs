using System.IO;

namespace Deckle.Core;

// Where an app's own data and its logs live, from its identity and the suite
// root. Pure, so a test hands it strings; AppPaths applies it to the real root.
//
//   suite app ("deckle")  data = <suite root>                  logs = <suite root>\diagnostics
//   any other app <id>    data = <suite root>\apps\<id>        logs = <suite root>\diagnostics\<id>
//
// The logs stay under one folder for the whole suite, so a person reads them
// in one place; the subfolder keeps one writer per file.
public sealed record AppRoots(string UserDataRoot, string DiagnosticsDirectory)
{
    public const string AppsFolderName = "apps";
    public const string DiagnosticsFolderName = "diagnostics";

    public static AppRoots For(AppIdentity identity, string suiteRoot)
    {
        string diagnostics = Path.Combine(suiteRoot, DiagnosticsFolderName);
        return identity.IsSuiteApp
            ? new AppRoots(suiteRoot, diagnostics)
            : new AppRoots(
                Path.Combine(suiteRoot, AppsFolderName, identity.Id),
                Path.Combine(diagnostics, identity.Id));
    }
}
