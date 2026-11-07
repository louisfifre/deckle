using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Deckle.Catalog;
using Deckle.Core;

namespace Deckle.Settings;

// ─── AudioCorpusConsentDialog ─────────────────────────────────────────────
//
// Opt-in consent for the WAV-audio side of corpus logging. Audio is a stronger
// privacy posture than text (voice is biometric-adjacent), so it has its own
// dialog and the person consents to each data class explicitly.
//
// The <profile> segment of the where path is a literal placeholder for the
// user, never translated.
internal static class AudioCorpusConsentDialog
{
    public static Task<bool> ShowAsync(XamlRoot root)
    {

        return ConsentService.RequestAsync(root, new ConsentRequest(
            Title:  Loc.Get("AudioCorpusConsent_Title"),
            Intro:  Loc.Get("AudioCorpusConsent_Body_Intro"),
            What:   Loc.Get("AudioCorpusConsent_Body_What"),
            Where:  Path.Combine(CorpusPaths.GetDirectoryPath(), "<profile>", "audio"),
            Remind: Loc.Get("AudioCorpusConsent_Body_Remind")));
    }
}
