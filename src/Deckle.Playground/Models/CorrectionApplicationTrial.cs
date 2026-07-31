using System.Globalization;

namespace Deckle.Playground;

internal enum CorrectionApplicationState
{
    ArmedSafe,
    Poisoned,
    Cancelled,
    Releasing,
    Completed,
}

internal enum CorrectionApplicationOutcome
{
    Applied,
    Abstained,
    IntegrityFailure,
    Cancelled,
}

internal enum CorrectionApplicationReason
{
    None,
    InvalidSentenceRange,
    InvalidEditRange,
    LiteralMismatch,
    EmptyReplacement,
    NoChange,
    InvalidTextElementBoundary,
    InitialSelection,
    InitialFocus,
    InitialWindowActivation,
    InitialReadOnly,
    InitialComposition,
    NonAppendTextChange,
    SelectionChanged,
    FocusLost,
    WindowActivationChanged,
    ReadOnlyChanged,
    CompositionStarted,
    CompositionUncertain,
    TargetGenerationChanged,
    PrefixChanged,
    UnsupportedTomMapping,
    DiagnosticSentenceChanged,
    TargetRangeChanged,
    Navigation,
    Unloaded,
    Disposed,
    Superseded,
    UserCancelled,
    TextPostcondition,
    SelectionPostcondition,
    FocusPostcondition,
    UndoTextPostcondition,
    UndoSelectionPostcondition,
    RedoTextPostcondition,
    RedoSelectionPostcondition,
    ApiFailure,
}

[Flags]
internal enum CorrectionApplicationGateFailure
{
    None = 0,
    Text = 1 << 0,
    Selection = 1 << 1,
    Focus = 1 << 2,
    Activation = 1 << 3,
    Writable = 1 << 4,
    Composition = 1 << 5,
    TargetGeneration = 1 << 6,
    TomMapping = 1 << 7,
    DiagnosticSentence = 1 << 8,
    TargetRange = 1 << 9,
}

internal readonly record struct CorrectionApplicationEdit(
    int Start,
    int Length,
    string Literal,
    string Replacement)
{
    public int End => checked(Start + Length);

    public int LengthDelta => Replacement.Length - Length;
}

internal readonly record struct CorrectionApplicationSelection(
    int Start,
    int End,
    int Options)
{
    public bool IsDegenerate => Start == End;
}

internal readonly record struct CorrectionApplicationArmState(
    string Body,
    int SentenceStart,
    int SentenceLength,
    string SentenceLiteral,
    CorrectionApplicationEdit Edit,
    CorrectionApplicationSelection Selection,
    int TargetGeneration,
    long WindowActivationGeneration,
    bool HasEditorFocus,
    bool IsWindowActive,
    bool IsReadOnly,
    bool IsCompositionNeutral);

internal readonly record struct CorrectionApplicationReleaseState(
    string Body,
    CorrectionApplicationSelection Selection,
    int TargetGeneration,
    long WindowActivationGeneration,
    bool HasEditorFocus,
    bool IsWindowActive,
    bool IsReadOnly,
    bool IsCompositionNeutral,
    bool IsTomMappingExact,
    bool IsDiagnosticSentenceExact,
    bool IsTargetRangeExact);

internal sealed record CorrectionApplicationPlan(
    string BeforeBody,
    string ExpectedBody,
    CorrectionApplicationSelection BeforeSelection,
    CorrectionApplicationSelection ExpectedSelection,
    CorrectionApplicationEdit Edit)
{
    public bool MatchesAppliedBody(string body)
        => string.Equals(body, ExpectedBody, StringComparison.Ordinal);

    public bool MatchesAppliedSelection(CorrectionApplicationSelection selection)
        => selection == ExpectedSelection;

    public bool MatchesApplied(
        string body,
        CorrectionApplicationSelection selection)
        => MatchesAppliedBody(body) && MatchesAppliedSelection(selection);

    public bool MatchesUndoBody(string body)
        => string.Equals(body, BeforeBody, StringComparison.Ordinal);

    public bool MatchesUndoSelection(CorrectionApplicationSelection selection)
        => selection == BeforeSelection;

    public bool MatchesUndo(
        string body,
        CorrectionApplicationSelection selection)
        => MatchesUndoBody(body) && MatchesUndoSelection(selection);

    public CorrectionApplicationBodyIdentity ClassifyBody(string body)
        => MatchesUndoBody(body)
            ? CorrectionApplicationBodyIdentity.Before
            : MatchesAppliedBody(body)
                ? CorrectionApplicationBodyIdentity.Applied
                : CorrectionApplicationBodyIdentity.Other;

    public CorrectionApplicationSelectionIdentity ClassifySelection(
        CorrectionApplicationSelection selection)
    {
        bool matchesBefore = HasSameEndpoints(selection, BeforeSelection);
        bool matchesApplied = HasSameEndpoints(selection, ExpectedSelection);
        return (matchesBefore, matchesApplied) switch
        {
            (true, true) => CorrectionApplicationSelectionIdentity.Both,
            (true, false) => CorrectionApplicationSelectionIdentity.Before,
            (false, true) => CorrectionApplicationSelectionIdentity.Applied,
            _ => CorrectionApplicationSelectionIdentity.Other,
        };
    }

    public int AppliedOptionsDifference(CorrectionApplicationSelection selection)
        => selection.Options ^ ExpectedSelection.Options;

    public int UndoOptionsDifference(CorrectionApplicationSelection selection)
        => selection.Options ^ BeforeSelection.Options;

    private static bool HasSameEndpoints(
        CorrectionApplicationSelection left,
        CorrectionApplicationSelection right)
        => left.Start == right.Start && left.End == right.End;
}

internal readonly record struct CorrectionApplicationPreparation(
    bool IsApproved,
    CorrectionApplicationOutcome Outcome,
    CorrectionApplicationReason Reason,
    CorrectionApplicationPlan? Plan);

internal sealed class CorrectionApplicationTrial
{
    private string _lastObservedBody;

    private CorrectionApplicationTrial(CorrectionApplicationArmState arm)
    {
        ArmedBody = arm.Body;
        _lastObservedBody = arm.Body;
        SentenceStart = arm.SentenceStart;
        SentenceLength = arm.SentenceLength;
        SentenceLiteral = arm.SentenceLiteral;
        Edit = arm.Edit;
        TargetGeneration = arm.TargetGeneration;
        WindowActivationGeneration = arm.WindowActivationGeneration;
        State = CorrectionApplicationState.ArmedSafe;
    }

    public string ArmedBody { get; }

    public int SentenceStart { get; }

    public int SentenceLength { get; }

    public string SentenceLiteral { get; }

    public CorrectionApplicationEdit Edit { get; }

    public int TargetGeneration { get; }

    public long WindowActivationGeneration { get; }

    public CorrectionApplicationState State { get; private set; }

    public CorrectionApplicationOutcome? Outcome { get; private set; }

    public CorrectionApplicationReason Reason { get; private set; }

    public CorrectionApplicationGateFailure GateFailures { get; private set; }

    public int AppendedUtf16Units { get; private set; }

    public int TextChangeEventCount { get; private set; }

    public static bool TryArm(
        CorrectionApplicationArmState arm,
        out CorrectionApplicationTrial? trial,
        out CorrectionApplicationReason reason)
    {
        trial = null;
        reason = ValidateArm(arm);
        if (reason != CorrectionApplicationReason.None)
        {
            return false;
        }

        trial = new CorrectionApplicationTrial(arm);
        return true;
    }

    public void ObserveBodyChange(string body)
    {
        if (State is not (CorrectionApplicationState.ArmedSafe or CorrectionApplicationState.Poisoned))
        {
            return;
        }

        TextChangeEventCount++;
        if (body.Length <= _lastObservedBody.Length
            || !body.StartsWith(_lastObservedBody, StringComparison.Ordinal))
        {
            Poison(CorrectionApplicationReason.NonAppendTextChange);
            return;
        }

        int appended = body.Length - _lastObservedBody.Length;
        AppendedUtf16Units = checked(AppendedUtf16Units + appended);
        _lastObservedBody = body;
    }

    public void ObserveSelection(CorrectionApplicationSelection selection, int bodyLength)
    {
        if (State is not (CorrectionApplicationState.ArmedSafe or CorrectionApplicationState.Poisoned))
        {
            return;
        }

        if (!selection.IsDegenerate || selection.Start != bodyLength)
        {
            Poison(CorrectionApplicationReason.SelectionChanged);
        }
    }

    public void Poison(CorrectionApplicationReason reason)
    {
        if (State is not (CorrectionApplicationState.ArmedSafe or CorrectionApplicationState.Poisoned))
        {
            return;
        }

        GateFailures |= GateFailureFor(reason);
        if (State != CorrectionApplicationState.ArmedSafe)
        {
            return;
        }

        State = CorrectionApplicationState.Poisoned;
        Reason = reason;
    }

    public void Cancel(CorrectionApplicationReason reason)
    {
        if (State is CorrectionApplicationState.Cancelled or CorrectionApplicationState.Completed)
        {
            return;
        }

        State = CorrectionApplicationState.Cancelled;
        Outcome = CorrectionApplicationOutcome.Cancelled;
        Reason = reason;
    }

    public CorrectionApplicationPreparation Prepare(CorrectionApplicationReleaseState release)
    {
        if (State == CorrectionApplicationState.Cancelled)
        {
            return Rejected(CorrectionApplicationOutcome.Cancelled, Reason);
        }

        if (State == CorrectionApplicationState.Poisoned)
        {
            State = CorrectionApplicationState.Completed;
            Outcome = CorrectionApplicationOutcome.Abstained;
            return Rejected(CorrectionApplicationOutcome.Abstained, Reason);
        }

        if (State != CorrectionApplicationState.ArmedSafe)
        {
            return Rejected(
                CorrectionApplicationOutcome.Abstained,
                Reason == CorrectionApplicationReason.None
                    ? CorrectionApplicationReason.Superseded
                    : Reason);
        }

        CorrectionApplicationReason rejection = ValidateRelease(release);
        if (rejection != CorrectionApplicationReason.None)
        {
            GateFailures |= GateFailureFor(rejection);
            State = CorrectionApplicationState.Completed;
            Outcome = CorrectionApplicationOutcome.Abstained;
            Reason = rejection;
            return Rejected(CorrectionApplicationOutcome.Abstained, rejection);
        }

        int delta = Edit.LengthDelta;
        var expectedSelection = new CorrectionApplicationSelection(
            checked(release.Selection.Start + delta),
            checked(release.Selection.End + delta),
            release.Selection.Options);

        string expectedBody = string.Concat(
            release.Body.AsSpan(0, Edit.Start),
            Edit.Replacement.AsSpan(),
            release.Body.AsSpan(Edit.End));

        State = CorrectionApplicationState.Releasing;
        var plan = new CorrectionApplicationPlan(
            release.Body,
            expectedBody,
            release.Selection,
            expectedSelection,
            Edit);
        return new CorrectionApplicationPreparation(
            true,
            CorrectionApplicationOutcome.Applied,
            CorrectionApplicationReason.None,
            plan);
    }

    public void CompleteApplied()
    {
        Complete(CorrectionApplicationOutcome.Applied, CorrectionApplicationReason.None);
    }

    public void CompleteAbstained(CorrectionApplicationReason reason)
    {
        Complete(CorrectionApplicationOutcome.Abstained, reason);
    }

    public void CompleteIntegrityFailure(CorrectionApplicationReason reason)
    {
        Complete(CorrectionApplicationOutcome.IntegrityFailure, reason);
    }

    private static CorrectionApplicationReason ValidateArm(CorrectionApplicationArmState arm)
    {
        if (!IsRangeWithin(arm.Body, arm.SentenceStart, arm.SentenceLength)
            || !arm.Body.AsSpan(arm.SentenceStart, arm.SentenceLength).Equals(
                arm.SentenceLiteral.AsSpan(),
                StringComparison.Ordinal))
        {
            return CorrectionApplicationReason.InvalidSentenceRange;
        }

        if (!IsRangeWithin(arm.Body, arm.Edit.Start, arm.Edit.Length)
            || arm.Edit.Start < arm.SentenceStart
            || arm.Edit.End > checked(arm.SentenceStart + arm.SentenceLength))
        {
            return CorrectionApplicationReason.InvalidEditRange;
        }

        if (!arm.Body.AsSpan(arm.Edit.Start, arm.Edit.Length).Equals(
                arm.Edit.Literal.AsSpan(),
                StringComparison.Ordinal))
        {
            return CorrectionApplicationReason.LiteralMismatch;
        }

        if (arm.Edit.Replacement.Length == 0)
        {
            return CorrectionApplicationReason.EmptyReplacement;
        }

        if (string.Equals(arm.Edit.Literal, arm.Edit.Replacement, StringComparison.Ordinal))
        {
            return CorrectionApplicationReason.NoChange;
        }

        if (!IsTextElementBoundary(arm.Body, arm.Edit.Start)
            || !IsTextElementBoundary(arm.Body, arm.Edit.End))
        {
            return CorrectionApplicationReason.InvalidTextElementBoundary;
        }

        if (!arm.Selection.IsDegenerate || arm.Selection.Start != arm.Body.Length)
        {
            return CorrectionApplicationReason.InitialSelection;
        }

        if (!arm.HasEditorFocus)
        {
            return CorrectionApplicationReason.InitialFocus;
        }

        if (!arm.IsWindowActive)
        {
            return CorrectionApplicationReason.InitialWindowActivation;
        }

        if (arm.IsReadOnly)
        {
            return CorrectionApplicationReason.InitialReadOnly;
        }

        return arm.IsCompositionNeutral
            ? CorrectionApplicationReason.None
            : CorrectionApplicationReason.InitialComposition;
    }

    private CorrectionApplicationReason ValidateRelease(CorrectionApplicationReleaseState release)
    {
        if (release.TargetGeneration != TargetGeneration)
        {
            return CorrectionApplicationReason.TargetGenerationChanged;
        }

        if (!release.IsWindowActive
            || release.WindowActivationGeneration != WindowActivationGeneration)
        {
            return CorrectionApplicationReason.WindowActivationChanged;
        }

        if (!release.HasEditorFocus)
        {
            return CorrectionApplicationReason.FocusLost;
        }

        if (release.IsReadOnly)
        {
            return CorrectionApplicationReason.ReadOnlyChanged;
        }

        if (!release.IsCompositionNeutral)
        {
            return CorrectionApplicationReason.CompositionUncertain;
        }

        if (!release.Selection.IsDegenerate || release.Selection.Start != release.Body.Length)
        {
            return CorrectionApplicationReason.SelectionChanged;
        }

        if (!release.Body.StartsWith(ArmedBody, StringComparison.Ordinal)
            || !string.Equals(release.Body, _lastObservedBody, StringComparison.Ordinal))
        {
            return CorrectionApplicationReason.PrefixChanged;
        }

        if (!release.IsTomMappingExact)
        {
            return CorrectionApplicationReason.UnsupportedTomMapping;
        }

        if (!release.IsDiagnosticSentenceExact)
        {
            return CorrectionApplicationReason.DiagnosticSentenceChanged;
        }

        return release.IsTargetRangeExact
            ? CorrectionApplicationReason.None
            : CorrectionApplicationReason.TargetRangeChanged;
    }

    private void Complete(
        CorrectionApplicationOutcome outcome,
        CorrectionApplicationReason reason)
    {
        if (State != CorrectionApplicationState.Releasing)
        {
            return;
        }

        State = CorrectionApplicationState.Completed;
        Outcome = outcome;
        Reason = reason;
    }

    private static CorrectionApplicationPreparation Rejected(
        CorrectionApplicationOutcome outcome,
        CorrectionApplicationReason reason)
        => new(false, outcome, reason, null);

    private static bool IsRangeWithin(string text, int start, int length)
        => start >= 0 && length > 0 && start <= text.Length - length;

    private static bool IsTextElementBoundary(string text, int position)
    {
        if (position == text.Length)
        {
            return true;
        }

        if (position < 0 || position > text.Length)
        {
            return false;
        }

        return Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), position) >= 0;
    }

    private static CorrectionApplicationGateFailure GateFailureFor(
        CorrectionApplicationReason reason)
        => reason switch
        {
            CorrectionApplicationReason.NonAppendTextChange
                or CorrectionApplicationReason.PrefixChanged => CorrectionApplicationGateFailure.Text,
            CorrectionApplicationReason.SelectionChanged
                or CorrectionApplicationReason.InitialSelection => CorrectionApplicationGateFailure.Selection,
            CorrectionApplicationReason.FocusLost
                or CorrectionApplicationReason.InitialFocus => CorrectionApplicationGateFailure.Focus,
            CorrectionApplicationReason.WindowActivationChanged
                or CorrectionApplicationReason.InitialWindowActivation => CorrectionApplicationGateFailure.Activation,
            CorrectionApplicationReason.ReadOnlyChanged
                or CorrectionApplicationReason.InitialReadOnly => CorrectionApplicationGateFailure.Writable,
            CorrectionApplicationReason.CompositionStarted
                or CorrectionApplicationReason.CompositionUncertain
                or CorrectionApplicationReason.InitialComposition => CorrectionApplicationGateFailure.Composition,
            CorrectionApplicationReason.TargetGenerationChanged => CorrectionApplicationGateFailure.TargetGeneration,
            CorrectionApplicationReason.UnsupportedTomMapping => CorrectionApplicationGateFailure.TomMapping,
            CorrectionApplicationReason.DiagnosticSentenceChanged => CorrectionApplicationGateFailure.DiagnosticSentence,
            CorrectionApplicationReason.TargetRangeChanged
                or CorrectionApplicationReason.LiteralMismatch => CorrectionApplicationGateFailure.TargetRange,
            _ => CorrectionApplicationGateFailure.None,
        };
}
