using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class TaskDayProposalTests
{
    [Fact]
    public void TheProposedDayIsTwoDaysBeforeTheDeadline()
    {
        var deadline = On(11, 15);

        Assert.Equal(deadline.Day.AddDays(-TaskDayProposal.DaysBeforeDeadline), TaskDayProposal.For(deadline, Today));
        Assert.Equal(new DateOnly(2026, 11, 13), TaskDayProposal.For(deadline, Today));
    }

    [Fact]
    public void TheProposedDayIsNeverBeforeToday()
    {
        Assert.Equal(Today, TaskDayProposal.For(On(11, 11), Today));
    }

    [Fact]
    public void ADeadlineOfTodayProposesToday()
    {
        Assert.Equal(Today, TaskDayProposal.For(On(11, 10), Today));
    }

    [Fact]
    public void ADeadlineAlreadyPastProposesToday()
    {
        Assert.Equal(Today, TaskDayProposal.For(On(11, 2), Today));
    }

    [Fact]
    public void NoDeadlineProposesNoDay()
    {
        Assert.Null(TaskDayProposal.For(null, Today));
    }

    [Fact]
    public void ARowCarriesTheDayItsDeadlineProposes()
    {
        var state = Sorted((To(), MailJudgment.Asks("Colis au point relais jusqu'au 15", On(11, 15))));

        Assert.Equal(new DateOnly(2026, 11, 13), Assert.Single(Rows(state)).ProposedDay);
    }
}
