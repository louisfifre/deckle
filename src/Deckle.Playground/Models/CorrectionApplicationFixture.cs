namespace Deckle.Playground;

internal sealed record CorrectionApplicationFixture(
    string Name,
    string Body,
    int SentenceStart,
    string SentenceLiteral,
    CorrectionApplicationEdit Edit)
{
    public int SentenceLength => SentenceLiteral.Length;

    public override string ToString() => Name;
}

internal static class CorrectionApplicationFixtures
{
    public static IReadOnlyList<CorrectionApplicationFixture> All { get; } =
    [
        Create("Equal length", "Il est la. ", 0, "Il est la.", 7, 2, "la", "là"),
        Create("Length changing", "Il mange. ", 0, "Il mange.", 3, 5, "mange", "mangera"),
        Prefixed("Surrogate pair", "😀"),
        Prefixed("Combining sequence", "e\u0301"),
        Prefixed("Variation selector", "✈️"),
        Prefixed("ZWJ sequence", "👩‍💻"),
        Prefixed("Regional-indicator flag", "🇫🇷"),
        Prefixed("CR boundary", "A\r"),
        Prefixed("CRLF boundary", "A\r\n"),
        Prefixed("LF boundary", "A\n"),
        Prefixed("Line separator", "A\u2028"),
        Prefixed("Paragraph separator", "A\u2029"),
    ];

    private static CorrectionApplicationFixture Prefixed(string name, string prefix)
    {
        int sentenceStart = prefix.Length;
        return Create(
            name,
            prefix + "Il est la. ",
            sentenceStart,
            "Il est la.",
            sentenceStart + 7,
            2,
            "la",
            "là");
    }

    private static CorrectionApplicationFixture Create(
        string name,
        string body,
        int sentenceStart,
        string sentenceLiteral,
        int editStart,
        int editLength,
        string literal,
        string replacement)
        => new(
            name,
            body,
            sentenceStart,
            sentenceLiteral,
            new CorrectionApplicationEdit(
                editStart,
                editLength,
                literal,
                replacement));
}
