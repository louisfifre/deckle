using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── AutocorrectDecisionsConsentDialog ────────────────────────────────────
//
// Opt-in consent for the per-word autocorrect decision dataset. It captures the
// words typed on enrolled apps in clear text, so the wording is explicit.
internal static class AutocorrectDecisionsConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {

        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("AutocorrectDecisionsConsent_Title"),
            Intro:  Loc.Get("AutocorrectDecisionsConsent_Body_Intro"),
            What:   Loc.Get("AutocorrectDecisionsConsent_Body_What"),
            Where:  Path.Combine(CorpusPaths.GetDirectoryPath(), "autocorrect.decisions.jsonl"),
            Remind: Loc.Get("AutocorrectDecisionsConsent_Body_Remind")));
    }
}
