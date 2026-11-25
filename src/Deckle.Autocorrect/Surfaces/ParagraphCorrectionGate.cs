namespace Deckle.Autocorrect;

// The one decision of when paragraph correction acts. It is Autocorrect's:
// the Autocorrect page holds the switch, and the correction acts only where
// autocorrect itself does. The Rewriting switch no longer matters.
//
// It serves three gates of the App with one rule:
//   - the host (reconcile): surface null, nothing is known of the focused app
//     yet, so only what does not depend on it is asked;
//   - the start of a draft and the offer: the focused surface is known, and
//     the app must be one where autocorrect acts, on a text field that is not
//     a password.
// The local model's availability comes in as a fact, so this module does not
// reference the Rewrite module.
public static class ParagraphCorrectionGate
{
    public static bool IsActive(AutocorrectSettings settings, FocusedSurface? surface, bool ollamaAvailable)
    {
        if (!settings.ParagraphCorrection || !settings.Enabled || !ollamaAvailable)
            return false;

        if (surface is null) return true;

        // The same three conditions the engine puts before correcting a word
        // (AutocorrectEngine.Commit), the enrolled app being the one the
        // "Apps enrolled" page governs.
        return surface.IsTextEditable
            && !surface.IsPassword
            && settings.IsEnabledFor(surface.ProcessName);
    }
}
