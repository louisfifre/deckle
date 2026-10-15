namespace Deckle.Llm.Rewrite;

// Finds where the last complete sentence of a batch ends, so a batch never
// sends half a sentence to the model. A model asked to rewrite "Je pense
// que" alone either completes it with words the speaker never said or leaves
// a fragment the next batch's rewrite cannot reattach; carrying the fragment
// into the next batch gives the model the whole sentence once.
//
// A sentence ends on '.', '!', '?' or '…', optionally followed by closing
// quotes, parentheses or brackets (» ” " ) ]). French typography puts a space
// (regular, no-break or narrow no-break) before '»', so that one closer may
// follow the terminator across such a space; the others must touch it, which
// keeps an opening straight quote after a full stop from being read as a
// closer. The boundary must then be followed by whitespace or the end of the
// text: that keeps a decimal ("3.5") or a domain name ("deckle.app") from
// cutting a sentence. An abbreviation followed by a space ("M. Dupont") still
// reads as a boundary; the cost is one early cut, not lost text.
internal static class SentenceBoundary
{
    // Returns the text up to and including its last sentence boundary, and
    // the unfinished remainder after it (leading whitespace removed). When the
    // text already ends on a boundary, or has no boundary at all, the whole
    // text is Complete and Carry is empty.
    internal static (string Complete, string Carry) SplitUnfinishedTail(string text)
    {
        string body = text.TrimEnd();

        for (int i = body.Length - 1; i >= 0; i--)
        {
            if (!IsTerminator(body[i])) continue;

            int end = EndOfClosers(body, i + 1);
            if (end < body.Length && !char.IsWhiteSpace(body[end])) continue;

            if (end == body.Length) return (body, "");
            return (body[..end], body[end..].TrimStart());
        }

        return (body, "");
    }

    static bool IsTerminator(char c) => c is '.' or '!' or '?' or '…';

    static bool IsAdjacentCloser(char c) => c is '»' or '”' or '"' or ')' or ']';

    static bool IsSpaceBeforeGuillemet(char c) => c is ' ' or ' ' or ' ';

    static int EndOfClosers(string body, int start)
    {
        int end = start;
        while (end < body.Length)
        {
            if (IsAdjacentCloser(body[end]))
            {
                end++;
                continue;
            }

            int j = end;
            while (j < body.Length && IsSpaceBeforeGuillemet(body[j])) j++;
            if (j > end && j < body.Length && body[j] == '»')
            {
                end = j + 1;
                continue;
            }

            break;
        }
        return end;
    }
}
