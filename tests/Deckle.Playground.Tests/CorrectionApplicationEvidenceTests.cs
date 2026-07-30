using Deckle.Playground;
using Microsoft.UI.Text;
using Xunit;

namespace Deckle.Playground.Tests;

[Trait("Category", "unit")]
public sealed class CorrectionApplicationEvidenceTests
{
    [Fact]
    public void SplitPostconditionOraclesDistinguishTextAndSelection()
    {
        var plan = Plan();

        Assert.True(plan.MatchesAppliedBody("Il est là. "));
        Assert.False(plan.MatchesAppliedBody("Il est la. "));
        Assert.True(plan.MatchesAppliedSelection(Selection(13, SelectionOptions.Active)));
        Assert.False(plan.MatchesAppliedSelection(Selection(11, SelectionOptions.Active)));
        Assert.True(plan.MatchesUndoBody("Il est la. "));
        Assert.False(plan.MatchesUndoBody("Il est là. "));
        Assert.True(plan.MatchesUndoSelection(Selection(
            11,
            SelectionOptions.Active | SelectionOptions.Overtype)));
        Assert.False(plan.MatchesUndoSelection(Selection(13, SelectionOptions.Active)));
    }

    [Fact]
    public void BodyIdentitySeparatesBeforeAppliedAndOther()
    {
        var plan = Plan();

        Assert.Equal(
            CorrectionApplicationBodyIdentity.Before,
            plan.ClassifyBody("Il est la. "));
        Assert.Equal(
            CorrectionApplicationBodyIdentity.Applied,
            plan.ClassifyBody("Il est là. "));
        Assert.Equal(
            CorrectionApplicationBodyIdentity.Other,
            plan.ClassifyBody("Il est le. "));
    }

    [Fact]
    public void SelectionIdentityUsesEndpointsIndependentlyFromOptions()
    {
        var plan = Plan();

        Assert.Equal(
            CorrectionApplicationSelectionIdentity.Before,
            plan.ClassifySelection(Selection(11, SelectionOptions.Overtype)));
        Assert.Equal(
            CorrectionApplicationSelectionIdentity.Applied,
            plan.ClassifySelection(Selection(13, SelectionOptions.Overtype)));
        Assert.Equal(
            CorrectionApplicationSelectionIdentity.Other,
            plan.ClassifySelection(Selection(12, SelectionOptions.Overtype)));

        var equalLengthPlan = plan with
        {
            ExpectedSelection = plan.BeforeSelection,
        };
        Assert.Equal(
            CorrectionApplicationSelectionIdentity.Both,
            equalLengthPlan.ClassifySelection(Selection(11, SelectionOptions.Overtype)));
    }

    [Fact]
    public void OptionsDifferenceUsesTheExpectedStageOptions()
    {
        var plan = Plan();
        var observed = Selection(
            13,
            SelectionOptions.Active | SelectionOptions.AtEndOfLine);

        Assert.Equal(
            (int)SelectionOptions.AtEndOfLine,
            plan.AppliedOptionsDifference(observed));
        Assert.Equal(
            (int)(SelectionOptions.AtEndOfLine | SelectionOptions.Overtype),
            plan.UndoOptionsDifference(observed));
    }

    private static CorrectionApplicationPlan Plan()
        => new(
            BeforeBody: "Il est la. ",
            ExpectedBody: "Il est là. ",
            BeforeSelection: Selection(
                11,
                SelectionOptions.Active | SelectionOptions.Overtype),
            ExpectedSelection: Selection(13, SelectionOptions.Active),
            Edit: new CorrectionApplicationEdit(7, 2, "la", "là"));

    private static CorrectionApplicationSelection Selection(
        int position,
        SelectionOptions options)
        => new(position, position, (int)options);
}
