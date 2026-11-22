using Deckle.Transcription.Lexicon;
using Deckle.Transcription.Parakeet.Decoding;

namespace Deckle.Transcription.Parakeet;

// What the lexicon gives a call: the boost tree the decoder favors and the
// casing pass on its text. Both are rebuilt only when the terms, the strength,
// the switch or the model change, so a take with an unchanged lexicon pays a
// list comparison and nothing else. Read and written under _transcribeLock.
public sealed partial class ParakeetBackend
{
    private LexiconSnapshot? _lexicon;

    private sealed record LexiconSnapshot(
        IReadOnlyList<string> Terms,
        bool BoostEnabled,
        float Strength,
        ParakeetLoadedModel Model,
        LexiconBoostTree? Boost,
        LexiconRecaser Recaser);

    // A lexicon that cannot be prepared never costs the take: it decodes as if the list were empty.
    private LexiconSnapshot? TryResolveLexicon(ParakeetLoadedModel model)
    {
        try
        {
            return ResolveLexicon(model);
        }
        catch (Exception ex)
        {
            _lexicon = null;
            DeckleParakeetSource.Log.LexiconUnavailable(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private LexiconSnapshot ResolveLexicon(ParakeetLoadedModel model)
    {
        LexiconSettings settings = _host.Transcription.Lexicon ?? new LexiconSettings();
        IReadOnlyList<string> terms = _host.ResolveLexiconTerms();
        float strength = (float)Math.Clamp(
            double.IsFinite(settings.BoostStrength) ? settings.BoostStrength : LexiconSettings.DefaultBoostStrength,
            LexiconSettings.MinBoostStrength,
            LexiconSettings.MaxBoostStrength);

        if (_lexicon is { } current
            && ReferenceEquals(current.Model, model)
            && current.BoostEnabled == settings.BoostEnabled
            && current.Strength == strength
            && current.Terms.SequenceEqual(terms, StringComparer.Ordinal))
        {
            return current;
        }

        LexiconBoostTree? boost = settings.BoostEnabled && terms.Count > 0
            ? LexiconBoostTree.Build(terms, TokenTexts(model), strength)
            : null;
        _lexicon = new LexiconSnapshot(terms, settings.BoostEnabled, strength, model, boost, new LexiconRecaser(terms));
        return _lexicon;
    }

    private static string[] TokenTexts(ParakeetLoadedModel model)
    {
        var texts = new string[model.Vocabulary.Count];
        for (int id = 0; id < texts.Length; id++)
        {
            texts[id] = model.Vocabulary.TextOf(id);
        }
        return texts;
    }
}
