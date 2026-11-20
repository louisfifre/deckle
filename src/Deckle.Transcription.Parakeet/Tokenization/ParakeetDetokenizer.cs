using System.Text;

namespace Deckle.Transcription.Parakeet.Tokenization;

// Turns what the decoder emitted into text. It keeps two views of the same ids: the whole text,
// and the text token by token. Segment building needs the second one to cut a transcript after a
// sentence-ending token and still know which time frame that token came from, so each piece keeps
// the position of its source token in the input rather than being merged into a string.

/// <summary>Converts token ids to text, whole or piece by piece.</summary>
internal sealed class ParakeetDetokenizer(ParakeetVocabulary vocabulary)
{
    /// <summary>
    /// Text of each non-silent token, in order, each tagged with its index in <paramref name="tokenIds"/>.
    /// A piece that opens a word starts with a space; nothing is trimmed here.
    /// </summary>
    public IReadOnlyList<TokenPiece> Pieces(IReadOnlyList<int> tokenIds)
    {
        var pieces = new List<TokenPiece>(tokenIds.Count);
        for (int i = 0; i < tokenIds.Count; i++)
        {
            string text = vocabulary.TextOf(tokenIds[i]);
            if (text.Length > 0)
            {
                pieces.Add(new TokenPiece(i, text));
            }
        }

        return pieces;
    }

    /// <summary>Full text: pieces joined, runs of spaces collapsed to one, ends trimmed.</summary>
    public string Decode(IReadOnlyList<int> tokenIds) => Normalize(Pieces(tokenIds));

    /// <summary>Joins pieces into one normalized string; also used to render a slice of a transcript.</summary>
    public static string Normalize(IEnumerable<TokenPiece> pieces)
    {
        var builder = new StringBuilder();
        foreach (TokenPiece piece in pieces)
        {
            builder.Append(piece.Text);
        }

        return CollapseSpaces(builder.ToString());
    }

    private static string CollapseSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool previousWasSpace = true; // starts true so leading spaces are dropped
        foreach (char c in text)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }
            }
            else
            {
                builder.Append(c);
            }

            previousWasSpace = isSpace;
        }

        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}

/// <summary>Text of one emitted token and the index of that token in the id sequence it came from.</summary>
/// <param name="TokenIndex">Position of the source token in the input id list.</param>
/// <param name="Text">Its text; starts with a space when the token opens a word.</param>
internal readonly record struct TokenPiece(int TokenIndex, string Text)
{
    /// <summary>True when the piece ends with sentence punctuation, a place where a segment may be cut.</summary>
    public bool EndsSentence => Text.AsSpan().TrimEnd() is [.., '.' or '!' or '?' or '…'];
}
