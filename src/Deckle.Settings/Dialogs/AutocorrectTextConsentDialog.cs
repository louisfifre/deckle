using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── AutocorrectTextConsentDialog ─────────────────────────────────────────
//
// Opt-in consent for the verbatim typed-input capture on enrolled apps (never
// password fields), the heaviest text capture in the app. One consent covers
// two files, the typed-sentence corpus and the typing stream, so the where
// lists both.
internal static class AutocorrectTextConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {
        string directory = CorpusPaths.GetDirectoryPath();
        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("AutocorrectTextConsent_Title"),
            Intro:  Loc.Get("AutocorrectTextConsent_Body_Intro"),
            What:   Loc.Get("AutocorrectTextConsent_Body_What"),
            Where:  Path.Combine(directory, "autocorrect.text.jsonl")
                  + "\n" + Path.Combine(directory, "autocorrect.stream.jsonl"),
            Remind: Loc.Get("AutocorrectTextConsent_Body_Remind")));
    }
}
