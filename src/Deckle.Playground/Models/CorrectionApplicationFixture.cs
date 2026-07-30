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

internal enum CorrectionSurfaceFault
{
    None,
    FreshRangeBeforeWrite,
    AppliedTextPostcondition,
    AppliedSelectionPostcondition,
    UndoTextPostcondition,
    UndoSelectionPostcondition,
    RedoTextPostcondition,
    RedoSelectionPostcondition,
}

internal readonly record struct CorrectionSurfaceExecution(
    CorrectionApplicationReason Reason,
    bool WasWriteAttempted,
    bool? ExactAppliedText = null,
    bool? ExactAppliedSelection = null,
    bool? ExactUndoText = null,
    bool? ExactUndoSelection = null,
    bool? ExactRedoText = null,
    bool? ExactRedoSelection = null);

internal readonly record struct CorrectionApplicationResolution(
    CorrectionApplicationOutcome Outcome,
    CorrectionApplicationReason Reason);

internal static class CorrectionApplicationCompletion
{
    public static CorrectionApplicationResolution Resolve(
        CorrectionSurfaceExecution execution,
        CorrectionApplicationReason? releaseAuthorityFailure,
        bool hasEditorFocus)
    {
        if (!execution.WasWriteAttempted
            && execution.Reason != CorrectionApplicationReason.None)
        {
            return new(
                CorrectionApplicationOutcome.Abstained,
                execution.Reason);
        }

        if (execution.Reason != CorrectionApplicationReason.None)
        {
            return new(
                CorrectionApplicationOutcome.IntegrityFailure,
                execution.Reason);
        }

        if (releaseAuthorityFailure is CorrectionApplicationReason authorityFailure)
        {
            return new(
                CorrectionApplicationOutcome.IntegrityFailure,
                authorityFailure);
        }

        return hasEditorFocus
            ? new(
                CorrectionApplicationOutcome.Applied,
                CorrectionApplicationReason.None)
            : new(
                CorrectionApplicationOutcome.IntegrityFailure,
                CorrectionApplicationReason.FocusPostcondition);
    }
}

internal static class CorrectionEvidencePrivacy
{
    public static long? CoarsenMilliseconds(long? value, int bucketSize)
        => value is long measured
            ? checked((long)Math.Round(
                measured / (double)bucketSize,
                MidpointRounding.AwayFromZero) * bucketSize)
            : null;

    public static string CountBucket(int value)
        => value switch
        {
            0 => "0",
            <= 4 => "1-4",
            <= 16 => "5-16",
            _ => "17+",
        };
}
