using System.Collections.Generic;
using System.Text;

namespace Deckle.Hud;

// The utterances of the current take, assembled exactly as the pipeline
// assembles the pasted text, so the panel shows what will be pasted: a blank
// line after an utterance cut on silence, a space otherwise.
//
// The character budget only bounds memory on a long take; the visible
// trimming is the panel's viewport. Whole utterances leave from the oldest
// end and the newest is always kept, even when it alone exceeds the budget.
internal sealed class TranscriptHistory(int maxCharacters)
{
    private readonly LinkedList<(string Text, bool EndedOnSilence)> _utterances = new();
    private string _text = "";

    public int UtteranceCount => _utterances.Count;

    public string Text => _text;

    public void Append(string text, bool endedOnSilence)
    {
        _utterances.AddLast((text, endedOnSilence));
        _text = Assemble();

        while (_text.Length > maxCharacters && _utterances.Count > 1)
        {
            _utterances.RemoveFirst();
            _text = Assemble();
        }
    }

    public void Clear()
    {
        _utterances.Clear();
        _text = "";
    }

    private string Assemble()
    {
        var builder = new StringBuilder();
        bool previousEndedOnSilence = false;
        foreach (var (text, endedOnSilence) in _utterances)
        {
            if (builder.Length > 0)
                builder.Append(previousEndedOnSilence ? "\n\n" : " ");
            builder.Append(text);
            previousEndedOnSilence = endedOnSilence;
        }
        return builder.ToString();
    }
}
