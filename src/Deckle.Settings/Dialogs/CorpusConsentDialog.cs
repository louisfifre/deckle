using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── CorpusConsentDialog ──────────────────────────────────────────────────
//
// Opt-in consent for corpus logging. A consent prompt, not a warning: no
// severity icon, no "Don't show again". The shape and the safe default
// (Cancel) live in ConsentService; this file owns the words and the path.
internal static class CorpusConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {

        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("CorpusConsent_Title"),
            Intro:  Loc.Get("CorpusConsent_Body_Intro"),
            What:   Loc.Get("CorpusConsent_Body_What"),
            Where:  CorpusPaths.GetDirectoryPath(),
            Remind: Loc.Get("CorpusConsent_Body_Remind")));
    }
}
