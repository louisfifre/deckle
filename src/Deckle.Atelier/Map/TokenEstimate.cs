namespace Deckle.Atelier;

// How many tokens a text is likely to take, without a model and without
// sending anything anywhere: its characters divided by four, rounded up.
//
// Why a ratio and not a tokenizer. Claude's tokenizer is not published: the
// only count Anthropic documents is the remote count_tokens endpoint, and
// even it says "The token count is an estimate"; Claude 4.7 and later
// produce about 30 % more tokens for the same text than earlier models
// (https://platform.claude.com/docs/en/build-with-claude/token-counting).
// A local library such as Microsoft.ML.Tokenizers counts exactly for OpenAI's
// and Llama's vocabularies only, and brings a package plus a vocabulary
// package; for the agents this map is about it would still be an estimate.
// Four characters per token is the ratio OpenAI documents for English
// (https://help.openai.com/en/articles/4936856-what-are-tokens-and-how-to-count-them).
// French and code usually take more tokens than this says, and a newer
// Claude model more again, so the figure reads as an order of magnitude;
// the view shows it as an estimate.
public static class TokenEstimate
{
    public const int CharactersPerToken = 4;

    public static int Of(string text) => (text.Length + CharactersPerToken - 1) / CharactersPerToken;
}

// Lines as an editor numbers them: a last line without its line break still
// counts, an empty file has none.
internal static class TextLines
{
    public static int Count(string text)
    {
        if (text.Length == 0) return 0;
        int lines = text.AsSpan().Count('\n');
        return text[^1] == '\n' ? lines : lines + 1;
    }

    public static string[] Split(string text) => text.Replace("\r\n", "\n").Split('\n');

    // The line, counted from 1, that holds the character at index.
    public static int At(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;
}
