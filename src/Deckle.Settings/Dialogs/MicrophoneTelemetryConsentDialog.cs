using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── MicrophoneTelemetryConsentDialog ─────────────────────────────────────
//
// Opt-in consent for the per-Recording microphone telemetry stream.
internal static class MicrophoneTelemetryConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {

        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("MicrophoneTelemetryConsent_Title"),
            Intro:  Loc.Get("MicrophoneTelemetryConsent_Body_Intro"),
            What:   Loc.Get("MicrophoneTelemetryConsent_Body_What"),
            Where:  Path.Combine(CorpusPaths.GetDirectoryPath(), "microphone.jsonl"),
            Remind: Loc.Get("MicrophoneTelemetryConsent_Body_Remind")));
    }
}
