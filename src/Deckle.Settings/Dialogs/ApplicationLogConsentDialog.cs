using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Diagnostics;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── ApplicationLogConsentDialog ──────────────────────────────────────────
//
// Opt-in consent for writing the full application log to disk.
internal static class ApplicationLogConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {

        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("ApplicationLogConsent_Title"),
            Intro:  Loc.Get("ApplicationLogConsent_Body_Intro"),
            What:   Loc.Get("ApplicationLogConsent_Body_What"),
            Where:  Path.Combine(AppPaths.DiagnosticsDirectory, DiagnosticsFiles.Trace),
            Remind: Loc.Get("ApplicationLogConsent_Body_Remind")));
    }
}
