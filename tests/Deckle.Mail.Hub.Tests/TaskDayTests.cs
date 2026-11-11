using Xunit;

namespace Deckle.Mail.Hub.Tests;

// The proposed day, the menu of days and the way a date is written.
[Trait("Category", "unit")]
public sealed class TaskDayTests
{
    private static readonly DateOnly Monday9 = new(2026, 11, 9);

    [Theory]
    [InlineData(15, 13)] // two days before the deadline
    [InlineData(10, 9)]  // never before today
    [InlineData(9, 9)]   // a deadline of today proposes today
    [InlineData(2, 9)]   // a deadline already past proposes today
    public void TheProposedDayIsTwoDaysBeforeTheDeadlineAndNeverBeforeToday(int deadlineDay, int proposedDay)
    {
        Assert.Equal(new DateOnly(2026, 11, proposedDay), TaskDays.Propose(new DateOnly(2026, 11, deadlineDay), Monday9));
    }

    [Fact]
    public void WithoutADeadlineNoDayIsProposed()
    {
        Assert.Null(TaskDays.Propose(null, Monday9));
    }

    [Fact]
    public void AProposedDayOpensTheMenuAsItsFirstLineAlreadySelected()
    {
        IReadOnlyList<TaskDayChoice> menu = TaskDays.Menu(new DateOnly(2026, 11, 13), Monday9);

        Assert.Equal(
            [TaskDayChoiceKind.Proposed, TaskDayChoiceKind.Today, TaskDayChoiceKind.Tomorrow, TaskDayChoiceKind.PickADay, TaskDayChoiceKind.NoDay],
            menu.Select(c => c.Kind));
        Assert.True(menu[0].IsSelected);
        Assert.Single(menu, c => c.IsSelected);
    }

    [Fact]
    public void AProposedTodayIsTheTodayLineItselfSelected()
    {
        IReadOnlyList<TaskDayChoice> menu = TaskDays.Menu(Monday9, Monday9);

        Assert.Equal(TaskDayChoiceKind.Today, menu[0].Kind);
        Assert.True(menu[0].IsSelected);
        Assert.DoesNotContain(menu, c => c.Kind == TaskDayChoiceKind.Proposed);
    }

    [Theory]
    [InlineData(2026, 11, 13, "Fri 13")]
    [InlineData(2026, 12, 1, "Tue 1 Dec")]
    [InlineData(2026, 12, 30, "Wed 30 Dec")]
    public void ADateIsTheWeekdayAndTheDayWithTheMonthWhenItIsAnother(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, MailDates.Day(new DateOnly(y, m, d), Monday9));
    }
}
