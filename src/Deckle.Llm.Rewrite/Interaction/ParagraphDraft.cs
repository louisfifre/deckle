using System.Text;

namespace Deckle.Llm.Rewrite;

// The observed text between two paragraph returns. It deliberately models only
// forward typing plus Backspace; any caret move or opaque editing gesture resets
// it, because an incomplete model must never drive a later replacement.
public sealed class ParagraphDraft
{
    internal const int Capacity = 4096;

    private readonly StringBuilder _text = new();
    private bool _valid = true;

    public bool IsEmpty => _text.Length == 0;

    public void Append(string text)
    {
        if (!_valid || string.IsNullOrEmpty(text)) return;
        if (_text.Length + text.Length > Capacity)
        {
            Invalidate();
            return;
        }
        _text.Append(text);
    }

    public void Backspace()
    {
        if (!_valid || _text.Length == 0)
        {
            Invalidate();
            return;
        }

        int remove = _text.Length >= 2
            && char.IsSurrogatePair(_text[^2], _text[^1])
            ? 2
            : 1;
        _text.Length -= remove;
    }

    public bool TryClose(out string paragraph)
    {
        paragraph = _valid ? _text.ToString() : string.Empty;
        Reset();
        return !string.IsNullOrWhiteSpace(paragraph);
    }

    public void Invalidate()
    {
        _text.Clear();
        _valid = false;
    }

    public void Reset()
    {
        _text.Clear();
        _valid = true;
    }

    /// <summary>Mirrors a bounded correction already applied to the observed
    /// paragraph. The last matching occurrence is the safest available slot;
    /// absence invalidates the draft instead of guessing.</summary>
    public void ApplyCorrection(string original, string replacement)
    {
        if (!_valid || string.IsNullOrEmpty(original)) return;
        string current = _text.ToString();
        int index = current.IndexOf(original, StringComparison.Ordinal);
        if (index < 0 || index != current.LastIndexOf(original, StringComparison.Ordinal))
        {
            Invalidate();
            return;
        }

        _text.Remove(index, original.Length);
        _text.Insert(index, replacement);
    }
}
