namespace Deckle.Playground;

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

internal enum CorrectionApplicationBodyIdentity
{
    Before,
    Applied,
    Other,
}

internal enum CorrectionApplicationSelectionIdentity
{
    Before,
    Applied,
    Both,
    Other,
}

internal readonly record struct CorrectionApplicationHistoryObservation(
    CorrectionApplicationBodyIdentity BodyIdentity,
    CorrectionApplicationSelectionIdentity SelectionIdentity,
    int ExpectedOptionsDifference,
    bool CanUndo,
    bool CanRedo);

internal readonly record struct CorrectionSurfaceExecution(
    CorrectionApplicationReason Reason,
    bool WasWriteAttempted,
    bool? ExactAppliedText = null,
    bool? ExactAppliedSelection = null,
    bool? ExactUndoText = null,
    bool? ExactUndoSelection = null,
    bool? ExactRedoText = null,
    bool? ExactRedoSelection = null,
    bool? CanUndoBeforeWrite = null,
    bool? CanRedoBeforeWrite = null,
    CorrectionApplicationHistoryObservation? AppliedObservation = null,
    CorrectionApplicationHistoryObservation? UndoObservation = null,
    CorrectionApplicationHistoryObservation? RedoObservation = null);

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
