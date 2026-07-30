using Deckle.Playground;
using Xunit;

namespace Deckle.Playground.Tests;

[Trait("Category", "unit")]
public sealed class CorrectionApplicationTrialTests
{
    [Fact]
    public void AppendOnlyContinuationPreparesOneExactEdit()
    {
        var trial = Arm();

        trial.ObserveBodyChange("Il est la. Je");
        trial.ObserveSelection(Selection(13), 13);

        var preparation = trial.Prepare(Release("Il est la. Je", Selection(13)));

        Assert.True(preparation.IsApproved);
        Assert.Equal("Il est là. Je", preparation.Plan!.ExpectedBody);
        Assert.Equal(Selection(13), preparation.Plan.ExpectedSelection);
        Assert.Equal(2, trial.AppendedUtf16Units);
        Assert.Equal(1, trial.TextChangeEventCount);
    }

    [Fact]
    public void LengthChangingEditMovesSemanticCaretByUtf16Delta()
    {
        var trial = Arm(
            body: "Il mange. ",
            sentence: "Il mange.",
            edit: new CorrectionApplicationEdit(3, 5, "mange", "mangera"));

        trial.ObserveBodyChange("Il mange. Puis");
        trial.ObserveSelection(Selection(14), 14);

        var preparation = trial.Prepare(Release("Il mange. Puis", Selection(14)));

        Assert.True(preparation.IsApproved);
        Assert.Equal("Il mangera. Puis", preparation.Plan!.ExpectedBody);
        Assert.Equal(Selection(16), preparation.Plan.ExpectedSelection);
    }

    [Fact]
    public void NonAppendChangePoisonsEvenWhenTextIsRestored()
    {
        var trial = Arm();

        trial.ObserveBodyChange("Il est l. ");
        trial.ObserveBodyChange("Il est la. ");

        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));

        Assert.False(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationOutcome.Abstained, preparation.Outcome);
        Assert.Equal(CorrectionApplicationReason.NonAppendTextChange, preparation.Reason);
    }

    [Fact]
    public void SelectionMovePoisonsEvenWhenCaretReturnsToEnd()
    {
        var trial = Arm();

        trial.ObserveSelection(new CorrectionApplicationSelection(2, 4, 0), 11);
        trial.ObserveSelection(Selection(11), 11);

        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));

        Assert.False(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationReason.SelectionChanged, preparation.Reason);
    }

    [Theory]
    [InlineData((int)CorrectionApplicationReason.FocusLost)]
    [InlineData((int)CorrectionApplicationReason.WindowActivationChanged)]
    [InlineData((int)CorrectionApplicationReason.ReadOnlyChanged)]
    [InlineData((int)CorrectionApplicationReason.CompositionStarted)]
    [InlineData((int)CorrectionApplicationReason.CompositionUncertain)]
    public void MonotonicAuthorityFailureAbstains(int reasonValue)
    {
        var reason = (CorrectionApplicationReason)reasonValue;
        var trial = Arm();

        trial.Poison(reason);

        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));
        Assert.False(preparation.IsApproved);
        Assert.Equal(reason, preparation.Reason);
    }

    [Fact]
    public void LifecycleCancellationOverridesEarlierPoison()
    {
        var trial = Arm();
        trial.Poison(CorrectionApplicationReason.CompositionUncertain);

        trial.Cancel(CorrectionApplicationReason.Unloaded);
        trial.Poison(CorrectionApplicationReason.ReadOnlyChanged);

        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));
        Assert.False(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationOutcome.Cancelled, preparation.Outcome);
        Assert.Equal(CorrectionApplicationReason.Unloaded, preparation.Reason);
        Assert.False(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Writable));
    }

    [Fact]
    public void MultipleGateFailuresRemainRecordedAfterFirstPoisonAndRestore()
    {
        var trial = Arm();

        trial.Poison(CorrectionApplicationReason.FocusLost);
        trial.Poison(CorrectionApplicationReason.ReadOnlyChanged);
        trial.Poison(CorrectionApplicationReason.CompositionStarted);
        trial.ObserveSelection(new CorrectionApplicationSelection(3, 5, 0), 11);
        trial.ObserveBodyChange("Il est l. ");
        trial.ObserveBodyChange("Il est la. ");

        Assert.Equal(CorrectionApplicationReason.FocusLost, trial.Reason);
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Focus));
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Writable));
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Composition));
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Selection));
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Text));

        trial.Cancel(CorrectionApplicationReason.UserCancelled);
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Focus));
        Assert.True(trial.GateFailures.HasFlag(CorrectionApplicationGateFailure.Writable));
    }

    [Fact]
    public void TargetGenerationDriftAbstains()
    {
        var trial = Arm();
        var release = Release("Il est la. ", Selection(11)) with { TargetGeneration = 8 };

        var preparation = trial.Prepare(release);

        Assert.False(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationReason.TargetGenerationChanged, preparation.Reason);
    }

    [Fact]
    public void WindowGenerationDriftAbstainsEvenWhenWindowIsActiveAgain()
    {
        var trial = Arm();
        var release = Release("Il est la. ", Selection(11)) with
        {
            WindowActivationGeneration = 12,
            IsWindowActive = true,
        };

        var preparation = trial.Prepare(release);

        Assert.False(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationReason.WindowActivationChanged, preparation.Reason);
    }

    [Theory]
    [InlineData(false, true, true, (int)CorrectionApplicationReason.UnsupportedTomMapping)]
    [InlineData(true, false, true, (int)CorrectionApplicationReason.DiagnosticSentenceChanged)]
    [InlineData(true, true, false, (int)CorrectionApplicationReason.TargetRangeChanged)]
    public void ExactRangeAuthoritiesAreIndependent(
        bool mapping,
        bool sentence,
        bool target,
        int expectedValue)
    {
        var expected = (CorrectionApplicationReason)expectedValue;
        var trial = Arm();
        var release = Release("Il est la. ", Selection(11)) with
        {
            IsTomMappingExact = mapping,
            IsDiagnosticSentenceExact = sentence,
            IsTargetRangeExact = target,
        };

        var preparation = trial.Prepare(release);

        Assert.False(preparation.IsApproved);
        Assert.Equal(expected, preparation.Reason);
    }

    [Fact]
    public void AppliedAndUndoPostconditionsRemainDistinct()
    {
        var trial = Arm();
        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));
        var plan = preparation.Plan!;

        Assert.True(plan.MatchesApplied("Il est là. ", Selection(11)));
        Assert.False(plan.MatchesApplied("Il est là. X", Selection(12)));
        Assert.True(plan.MatchesUndo("Il est la. ", Selection(11)));
        Assert.False(plan.MatchesUndo("Il est la. X", Selection(12)));
    }

    [Fact]
    public void FreshRangeFailureBeforeWriteCompletesAsAbstention()
    {
        var trial = Arm();
        var preparation = trial.Prepare(Release("Il est la. ", Selection(11)));

        trial.CompleteAbstained(CorrectionApplicationReason.TargetRangeChanged);

        Assert.True(preparation.IsApproved);
        Assert.Equal(CorrectionApplicationState.Completed, trial.State);
        Assert.Equal(CorrectionApplicationOutcome.Abstained, trial.Outcome);
        Assert.Equal(CorrectionApplicationReason.TargetRangeChanged, trial.Reason);
    }

    [Fact]
    public void EveryCalibrationFixtureProducesOneExactUtf16Plan()
    {
        foreach (var fixture in CorrectionApplicationFixtures.All)
        {
            var arm = ValidArm(
                fixture.Body,
                fixture.SentenceLiteral,
                fixture.Edit,
                fixture.SentenceStart,
                Selection(fixture.Body.Length));
            Assert.True(
                CorrectionApplicationTrial.TryArm(arm, out var trial, out var reason),
                $"{fixture.Name}: {reason}");

            var release = new CorrectionApplicationReleaseState(
                fixture.Body,
                Selection(fixture.Body.Length),
                TargetGeneration: 7,
                WindowActivationGeneration: 11,
                HasEditorFocus: true,
                IsWindowActive: true,
                IsReadOnly: false,
                IsCompositionNeutral: true,
                IsTomMappingExact: true,
                IsDiagnosticSentenceExact: true,
                IsTargetRangeExact: true);
            var preparation = trial!.Prepare(release);
            string expected = string.Concat(
                fixture.Body.AsSpan(0, fixture.Edit.Start),
                fixture.Edit.Replacement.AsSpan(),
                fixture.Body.AsSpan(fixture.Edit.End));

            Assert.True(preparation.IsApproved, fixture.Name);
            Assert.Equal(expected, preparation.Plan!.ExpectedBody);
            Assert.Equal(
                fixture.Body.Length + fixture.Edit.LengthDelta,
                preparation.Plan.ExpectedSelection.Start);
        }
    }

    [Fact]
    public void FreshRangeFaultResolvesToAbstentionBeforeWrite()
    {
        var execution = new CorrectionSurfaceExecution(
            CorrectionApplicationReason.TargetRangeChanged,
            WasWriteAttempted: false);

        var resolution = CorrectionApplicationCompletion.Resolve(
            execution,
            releaseAuthorityFailure: null,
            hasEditorFocus: true);

        Assert.Equal(CorrectionApplicationOutcome.Abstained, resolution.Outcome);
        Assert.Equal(CorrectionApplicationReason.TargetRangeChanged, resolution.Reason);
    }

    [Theory]
    [InlineData((int)CorrectionApplicationReason.TextPostcondition)]
    [InlineData((int)CorrectionApplicationReason.SelectionPostcondition)]
    [InlineData((int)CorrectionApplicationReason.UndoTextPostcondition)]
    [InlineData((int)CorrectionApplicationReason.UndoSelectionPostcondition)]
    [InlineData((int)CorrectionApplicationReason.RedoTextPostcondition)]
    [InlineData((int)CorrectionApplicationReason.RedoSelectionPostcondition)]
    [InlineData((int)CorrectionApplicationReason.ApiFailure)]
    public void EveryPostwriteFaultResolvesToIntegrityFailure(int reasonValue)
    {
        var reason = (CorrectionApplicationReason)reasonValue;
        var execution = new CorrectionSurfaceExecution(reason, WasWriteAttempted: true);

        var resolution = CorrectionApplicationCompletion.Resolve(
            execution,
            releaseAuthorityFailure: null,
            hasEditorFocus: true);

        Assert.Equal(CorrectionApplicationOutcome.IntegrityFailure, resolution.Outcome);
        Assert.Equal(reason, resolution.Reason);
    }

    [Fact]
    public void FailedUndoTextLeavesLaterObservationsUnknown()
    {
        var execution = new CorrectionSurfaceExecution(
            CorrectionApplicationReason.UndoTextPostcondition,
            WasWriteAttempted: true,
            ExactAppliedText: true,
            ExactAppliedSelection: true,
            ExactUndoText: false);

        Assert.True(execution.ExactAppliedText);
        Assert.True(execution.ExactAppliedSelection);
        Assert.False(execution.ExactUndoText);
        Assert.Null(execution.ExactUndoSelection);
        Assert.Null(execution.ExactRedoText);
        Assert.Null(execution.ExactRedoSelection);
    }

    [Theory]
    [MemberData(nameof(UnicodePrefixCases))]
    public void UnicodeSequencesOutsideEditRemainExact(string prefix)
    {
        string body = $"{prefix} Il est la. ";
        int sentenceStart = prefix.Length + 1;
        var edit = new CorrectionApplicationEdit(sentenceStart + 7, 2, "la", "là");
        var trial = Arm(body, "Il est la.", edit, sentenceStart);

        var preparation = trial.Prepare(Release(body, Selection(body.Length)));

        Assert.True(preparation.IsApproved);
        Assert.Equal($"{prefix} Il est là. ", preparation.Plan!.ExpectedBody);
    }

    public static TheoryData<string> UnicodePrefixCases => new()
    {
        "😀",
        "e\u0301",
        "✈️",
        "👩‍💻",
        "🇫🇷",
    };

    [Theory]
    [InlineData("😀x", 1)]
    [InlineData("e\u0301x", 1)]
    [InlineData("✈️x", 1)]
    [InlineData("👩‍💻x", 2)]
    [InlineData("🇫🇷x", 2)]
    public void EditCannotSplitStringInfoTextElement(string body, int split)
    {
        var arm = ValidArm(
            body,
            body,
            new CorrectionApplicationEdit(split, 1, body.Substring(split, 1), "z"),
            sentenceStart: 0,
            selection: Selection(body.Length));

        bool armed = CorrectionApplicationTrial.TryArm(arm, out _, out var reason);

        Assert.False(armed);
        Assert.Equal(CorrectionApplicationReason.InvalidTextElementBoundary, reason);
    }

    [Theory]
    [InlineData("A\rB")]
    [InlineData("A\r\nB")]
    [InlineData("A\nB")]
    [InlineData("A\u2028B")]
    [InlineData("A\u2029B")]
    public void LineBoundaryBodiesUseExactUtf16Planning(string prefix)
    {
        string body = prefix + "Il est la. ";
        int sentenceStart = prefix.Length;
        var trial = Arm(
            body,
            "Il est la.",
            new CorrectionApplicationEdit(sentenceStart + 7, 2, "la", "là"),
            sentenceStart);

        var preparation = trial.Prepare(Release(body, Selection(body.Length)));

        Assert.True(preparation.IsApproved);
        Assert.Equal(prefix + "Il est là. ", preparation.Plan!.ExpectedBody);
    }

    [Fact]
    public void ArmRejectsLiteralMismatch()
    {
        var arm = ValidArm(
            "Il est la. ",
            "Il est la.",
            new CorrectionApplicationEdit(7, 2, "le", "là"));

        bool armed = CorrectionApplicationTrial.TryArm(arm, out _, out var reason);

        Assert.False(armed);
        Assert.Equal(CorrectionApplicationReason.LiteralMismatch, reason);
    }

    [Theory]
    [InlineData(false, true, false, true, (int)CorrectionApplicationReason.InitialFocus)]
    [InlineData(true, false, false, true, (int)CorrectionApplicationReason.InitialWindowActivation)]
    [InlineData(true, true, true, true, (int)CorrectionApplicationReason.InitialReadOnly)]
    [InlineData(true, true, false, false, (int)CorrectionApplicationReason.InitialComposition)]
    public void ArmRequiresEveryInitialAuthority(
        bool focus,
        bool window,
        bool readOnly,
        bool composition,
        int expectedValue)
    {
        var expected = (CorrectionApplicationReason)expectedValue;
        var arm = ValidArm("Il est la. ", "Il est la.", DefaultEdit) with
        {
            HasEditorFocus = focus,
            IsWindowActive = window,
            IsReadOnly = readOnly,
            IsCompositionNeutral = composition,
        };

        bool armed = CorrectionApplicationTrial.TryArm(arm, out _, out var reason);

        Assert.False(armed);
        Assert.Equal(expected, reason);
    }

    private static CorrectionApplicationTrial Arm(
        string body = "Il est la. ",
        string sentence = "Il est la.",
        CorrectionApplicationEdit? edit = null,
        int sentenceStart = 0)
    {
        var arm = ValidArm(body, sentence, edit ?? DefaultEdit, sentenceStart);
        Assert.True(CorrectionApplicationTrial.TryArm(arm, out var trial, out var reason), reason.ToString());
        return trial!;
    }

    private static CorrectionApplicationArmState ValidArm(
        string body,
        string sentence,
        CorrectionApplicationEdit edit,
        int sentenceStart = 0,
        CorrectionApplicationSelection? selection = null)
        => new(
            body,
            sentenceStart,
            sentence.Length,
            sentence,
            edit,
            selection ?? Selection(body.Length),
            TargetGeneration: 7,
            WindowActivationGeneration: 11,
            HasEditorFocus: true,
            IsWindowActive: true,
            IsReadOnly: false,
            IsCompositionNeutral: true);

    private static CorrectionApplicationReleaseState Release(
        string body,
        CorrectionApplicationSelection selection)
        => new(
            body,
            selection,
            TargetGeneration: 7,
            WindowActivationGeneration: 11,
            HasEditorFocus: true,
            IsWindowActive: true,
            IsReadOnly: false,
            IsCompositionNeutral: true,
            IsTomMappingExact: true,
            IsDiagnosticSentenceExact: true,
            IsTargetRangeExact: true);

    private static CorrectionApplicationSelection Selection(int position)
        => new(position, position, Options: 0);

    private static CorrectionApplicationEdit DefaultEdit => new(7, 2, "la", "là");
}
