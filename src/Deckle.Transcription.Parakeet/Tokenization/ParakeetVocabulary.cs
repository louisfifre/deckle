using System.Globalization;

namespace Deckle.Transcription.Parakeet.Tokenization;

// The vocabulary of Parakeet TDT is a SentencePiece table exported as vocab.txt, one "token id"
// pair per line. Three things in it are not obvious:
//  - "▁" (U+2581) is SentencePiece's word-start marker: a token that begins with it opens a new
//    word, so it stands for a space in the output text. The bare "▁" token exists too.
//  - the blank ("<blk>") is the transducer's "emit nothing" symbol. Its id is the last of the
//    table, one past the real tokens, and the decoder needs it to know which joint output to skip.
//  - control tokens ("<unk>", "<pad>", "<|timestamp|>", "<|spltoken16|>"...) fill the first ids.
//    They carry no speech, so they never reach the text.
// The id is read from the line, not from its position, so a vocabulary with gaps still resolves.

/// <summary>Token table of a Parakeet model: id to piece, plus the blank id the decoder needs.</summary>
internal sealed class ParakeetVocabulary
{
    /// <summary>SentencePiece's word-start marker, which stands for a space in the output text.</summary>
    internal const char WordStartMarker = '▁';

    private const string BlankToken = "<blk>";

    private readonly string?[] _pieces;

    private ParakeetVocabulary(string?[] pieces, int blankId)
    {
        _pieces = pieces;
        BlankId = blankId;
    }

    /// <summary>Id of the blank symbol: the transducer's "no token emitted" output.</summary>
    public int BlankId { get; }

    /// <summary>Size of the id space, blank included: ids run from 0 to Count - 1.</summary>
    public int Count => _pieces.Length;

    /// <summary>Reads a vocab.txt file.</summary>
    public static ParakeetVocabulary Load(string path) => Parse(File.ReadLines(path));

    /// <summary>Parses "token id" lines; the id is what follows the last space, so a token may itself contain one.</summary>
    /// <exception cref="FormatException">A line has no id, an id is repeated, or no blank token is present.</exception>
    public static ParakeetVocabulary Parse(IEnumerable<string> lines)
    {
        var entries = new List<(string Token, int Id)>();
        int lineNumber = 0;
        foreach (string raw in lines)
        {
            lineNumber++;
            // A trailing carriage return or empty last line is file noise, not an entry.
            string line = raw.TrimEnd('\r', '\n');
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.LastIndexOf(' ');
            if (separator <= 0
                || !int.TryParse(line.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                throw new FormatException($"Vocabulary line {lineNumber} is not of the form \"token id\".");
            }

            entries.Add((line[..separator], id));
        }

        if (entries.Count == 0)
        {
            throw new FormatException("The vocabulary is empty.");
        }

        var pieces = new string?[entries.Max(e => e.Id) + 1];
        int blankId = -1;
        foreach ((string token, int id) in entries)
        {
            if (pieces[id] is not null)
            {
                throw new FormatException($"Vocabulary id {id} is defined twice.");
            }

            pieces[id] = token;
            if (token == BlankToken)
            {
                blankId = id;
            }
        }

        if (blankId < 0)
        {
            throw new FormatException($"The vocabulary has no {BlankToken} token.");
        }

        return new ParakeetVocabulary(pieces, blankId);
    }

    /// <summary>True when the id is the blank or a control token: it produces no text.</summary>
    public bool IsSilent(int id)
    {
        if (id == BlankId)
        {
            return true;
        }

        string? piece = PieceOrNull(id);
        return piece is null || IsControlToken(piece);
    }

    /// <summary>
    /// Text a token contributes, with its word-start marker turned into a space and not yet trimmed,
    /// so "▁Bon" gives " Bon". Empty for the blank, control tokens and ids outside the table.
    /// </summary>
    public string TextOf(int id)
    {
        if (IsSilent(id))
        {
            return string.Empty;
        }

        return _pieces[id]!.Replace(WordStartMarker, ' ');
    }

    private string? PieceOrNull(int id) => (uint)id < (uint)_pieces.Length ? _pieces[id] : null;

    // Control tokens are written <...>; a lone "<" or ">" is a real punctuation token, hence the length check.
    private static bool IsControlToken(string piece) =>
        piece.Length >= 3 && piece[0] == '<' && piece[^1] == '>';
}
