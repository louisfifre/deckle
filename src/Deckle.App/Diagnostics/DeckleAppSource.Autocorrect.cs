using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// The App-side composition of the autocorrect runtime. The engine's own
// lifecycle (arming, reranker status) stays on Deckle-Autocorrect.
public sealed partial class DeckleAppSource
{
    // The switch is on but the runtime was never built. reason:
    // input_host_missing (the shared Raw Input host was not created) or
    // lexicon_missing (the French lexicon file is absent from Data).
    [Event(EvtAutocorrectUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect is not running | reason={0}")]
    public void AutocorrectUnavailable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtAutocorrectUnavailable, reason);
    }

    // phase: build (loading lexicons and models) or activate (publishing
    // and arming the built runtime). Autocorrect stays off until the next
    // settings change retries.
    [Event(EvtAutocorrectFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect failed to start | phase={0} | {1}: {2}")]
    public void AutocorrectFailed(string phase, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAutocorrectFailed, phase, ex_type, ex_message);
    }

    // An optional asset was left out and autocorrect runs without what it
    // brings. asset: pair_bigrams, verb_morphology, sentence_judge or
    // pack:<id>. reason: file_missing or load_returned_null.
    [Event(EvtAutocorrectAssetSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Autocorrect runs without an optional asset | asset={0} | reason={1}")]
    public void AutocorrectAssetSkipped(string asset, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtAutocorrectAssetSkipped, asset, reason);
    }

    // Best-effort: the app stays undecided and is offered again next run.
    [Event(EvtAutocorrectEnrollmentPromptFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "autocorrect enrollment prompt failed | ex_type={0} | ex_message={1}")]
    public void AutocorrectEnrollmentPromptFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtAutocorrectEnrollmentPromptFailed, ex_type, ex_message);
    }
}
