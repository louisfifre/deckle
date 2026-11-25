using Deckle.Transcription;
using Xunit;

namespace Deckle.App.Tests;

// The quit gesture while a dictation is ending: the work is finished (the text
// is copied), then the app exits, and nothing runs between the two. The tests
// assert the order, never a duration.
[Trait("Category", "regression")]
public sealed class QuitSequenceTests
{
    [Fact]
    public void A_quit_exits_right_after_the_work_finished_with_nothing_between()
    {
        var steps = new List<string>();

        QuitSequence.Run(
            () => { steps.Add("finish"); return DrainResult.Finished; },
            ex => steps.Add("drain failed"),
            () => steps.Add("exit"));

        Assert.Equal(["finish", "exit"], steps);
    }

    [Fact]
    public void A_quit_whose_work_ran_past_the_bound_still_exits()
    {
        var steps = new List<string>();

        QuitSequence.Run(
            () => { steps.Add("finish"); return DrainResult.TimedOut; },
            ex => steps.Add("drain failed"),
            () => steps.Add("exit"));

        Assert.Equal(["finish", "exit"], steps);
    }

    [Fact]
    public void A_quit_whose_drain_throws_reports_it_then_exits()
    {
        var steps = new List<string>();

        QuitSequence.Run(
            () => { steps.Add("finish"); throw new InvalidOperationException("boom"); },
            ex => steps.Add("drain failed"),
            () => steps.Add("exit"));

        Assert.Equal(["finish", "drain failed", "exit"], steps);
    }
}
