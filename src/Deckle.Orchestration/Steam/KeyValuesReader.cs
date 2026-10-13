using System.Text;

namespace Deckle.Orchestration;

// Reads Valve KeyValues text, the format of Steam's libraryfolders.vdf and
// appmanifest_<appid>.acf files. Pure: it takes the text, returns the nodes,
// and throws FormatException (with the line number) on a malformed document.
//
// Grammar, as Steam writes these files:
//   document := node*
//   node     := key ( value | "{" node* "}" )
//   key, value := "quoted string" | bare-token
// Quoted strings understand \" \\ \n \t. "//" starts a comment to the end of
// the line. A line whose first token starts with "#include" or "#base" pulls
// in another file in Valve's tools; Steam's own files never use it, so the
// line is skipped. Whitespace and line breaks carry no meaning.
public static class KeyValuesReader
{
    public static IReadOnlyList<KeyValuesNode> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokenizer = new Tokenizer(text);
        List<KeyValuesNode> nodes = ParseBlock(tokenizer, nested: false);
        return nodes;
    }

    private static List<KeyValuesNode> ParseBlock(Tokenizer tokenizer, bool nested)
    {
        var nodes = new List<KeyValuesNode>();
        while (true)
        {
            Token token = tokenizer.Next();
            switch (token.Kind)
            {
                case TokenKind.End when nested:
                    throw Error(token.Line, "a block is not closed: '}' expected before the end of the text");
                case TokenKind.End:
                    return nodes;
                case TokenKind.Close when nested:
                    return nodes;
                case TokenKind.Close:
                    throw Error(token.Line, "'}' without a matching '{'");
                case TokenKind.Open:
                    throw Error(token.Line, "'{' where a key was expected");
            }

            string key = token.Text;
            Token next = tokenizer.Next();
            switch (next.Kind)
            {
                case TokenKind.String:
                    nodes.Add(new KeyValuesNode(key, next.Text));
                    break;
                case TokenKind.Open:
                    var block = new KeyValuesNode(key);
                    block.Children.AddRange(ParseBlock(tokenizer, nested: true));
                    nodes.Add(block);
                    break;
                default:
                    throw Error(next.Line, $"key \"{key}\" has no value: a string or '{{' was expected");
            }
        }
    }

    private static FormatException Error(int line, string reason) =>
        new($"KeyValues text is malformed at line {line}: {reason}.");

    private enum TokenKind { String, Open, Close, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Line);

    private sealed class Tokenizer(string text)
    {
        private int _position;
        private int _line = 1;

        public Token Next()
        {
            while (true)
            {
                SkipWhitespace();
                if (_position >= text.Length) return new Token(TokenKind.End, string.Empty, _line);

                char c = text[_position];
                if (c == '/' && Peek(1) == '/')
                {
                    SkipToEndOfLine();
                    continue;
                }
                if (c == '#' && (StartsWith("#include") || StartsWith("#base")))
                {
                    SkipToEndOfLine();
                    continue;
                }
                if (c == '{')
                {
                    _position++;
                    return new Token(TokenKind.Open, "{", _line);
                }
                if (c == '}')
                {
                    _position++;
                    return new Token(TokenKind.Close, "}", _line);
                }
                if (c == '"') return ReadQuoted();
                return ReadBare();
            }
        }

        private Token ReadQuoted()
        {
            int startLine = _line;
            _position++; // opening quote
            var builder = new StringBuilder();
            while (_position < text.Length)
            {
                char c = text[_position++];
                if (c == '"') return new Token(TokenKind.String, builder.ToString(), startLine);
                if (c == '\n') _line++;
                if (c == '\\' && _position < text.Length)
                {
                    char escaped = text[_position++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        default:
                            // Unknown escape: keep both characters, as Valve's reader does.
                            builder.Append('\\').Append(escaped);
                            if (escaped == '\n') _line++;
                            break;
                    }
                    continue;
                }
                builder.Append(c);
            }
            throw Error(startLine, "a quoted string is not closed");
        }

        private Token ReadBare()
        {
            int start = _position;
            while (_position < text.Length)
            {
                char c = text[_position];
                if (char.IsWhiteSpace(c) || c is '"' or '{' or '}') break;
                if (c == '/' && Peek(1) == '/') break;
                _position++;
            }
            return new Token(TokenKind.String, text[start.._position], _line);
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                if (text[_position] == '\n') _line++;
                _position++;
            }
        }

        private void SkipToEndOfLine()
        {
            while (_position < text.Length && text[_position] != '\n') _position++;
        }

        private char Peek(int offset) =>
            _position + offset < text.Length ? text[_position + offset] : '\0';

        private bool StartsWith(string word) =>
            string.CompareOrdinal(text, _position, word, 0, word.Length) == 0;
    }
}
