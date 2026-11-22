using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Modules;
using Deckle.Transcription;
using Deckle.Transcription.Lexicon;

namespace Deckle.App;

// The lexicon is shared between dictation and autocorrect without either
// module knowing the other: the App, which composes both, carries each one's
// words to the other. The dictation lexicon (TranscriptionSettings.Lexicon)
// reaches the autocorrect's correctors, and the proper nouns the autocorrect
// learned from typing reach the dictation.
public partial class App
{
    // The person's declared terms, for the autocorrect. Empty when the
    // transcription module is absent, so its settings file is never created
    // for a module that does not run.
    private static IEnumerable<string> DeclaredLexiconTerms() =>
        ModulePresence.IsPresent(ModuleIds.Transcription)
            ? LexiconTerms.Merge(TranscriptionSettingsService.Instance.Current.Lexicon?.Terms)
            : Array.Empty<string>();

    // The proper nouns the autocorrect adopted, for the dictation. A word that
    // carries a capital ("Anytype", "WinUI") is one the dictation should spell
    // that way; the dictionary keeps such words in their typed case. Empty while
    // the autocorrect is off or still building.
    private IEnumerable<string> LearnedProperNouns()
    {
        var runtime = _autocorrectRuntime;
        if (runtime is null) return Array.Empty<string>();
        try
        {
            return runtime.Dictionary.AdoptedWords.Where(LexiconTerms.HasCapital).ToList();
        }
        catch (ObjectDisposedException)
        {
            // The runtime was torn down between the read and the call: nothing learned to offer.
            return Array.Empty<string>();
        }
    }
}
