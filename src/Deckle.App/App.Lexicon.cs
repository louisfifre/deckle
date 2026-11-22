using System;
using System.Collections.Generic;
using Deckle.Modules;
using Deckle.Transcription;
using Deckle.Transcription.Lexicon;

namespace Deckle.App;

// The App carries the dictation lexicon (TranscriptionSettings.Lexicon) to the
// autocorrect's correctors; neither module knows the other.
public partial class App
{
    // The person's declared terms, for the autocorrect. Empty when the
    // transcription module is absent, so its settings file is never created
    // for a module that does not run.
    private static IEnumerable<string> DeclaredLexiconTerms() =>
        ModulePresence.IsPresent(ModuleIds.Transcription)
            ? LexiconTerms.Merge(TranscriptionSettingsService.Instance.Current.Lexicon?.Terms)
            : Array.Empty<string>();
}
