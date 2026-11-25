using Deckle.Autocorrect;

namespace Deckle.App;

// ── Self-check: the Autocorrect page with its switch on ──────────────────────
//
// A fresh data root has Autocorrect off, and the page then shows that one
// switch: the "Paragraph correction" section and the "Your words" card would
// never be seen by a review pass. So the run writes the switch On into its own
// root before the walk. Nothing starts from it: the keyboard hook, the engine
// and the lexicon load belong to InitializeAutocorrect, which the self-check
// never composes (it composes only the settings surface), so the write only
// changes what the page shows. Only this run's root is written, never the
// maintainer's.
public partial class App
{
    private static void SeedAutocorrectOn() =>
        AutocorrectSettingsService.Instance.SetEnabled(true);
}
