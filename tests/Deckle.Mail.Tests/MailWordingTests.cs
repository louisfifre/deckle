using Xunit;
using static Deckle.Mail.Tests.SyntheticMail;

namespace Deckle.Mail.Tests;

[Trait("Category", "unit")]
public class MailWordingTests
{
    [Fact]
    public void ADayOfTheCurrentMonthIsWrittenWithItsWeekdayAndDay()
    {
        Assert.Equal("Fri 13", MailWording.Day(new DateOnly(2026, 11, 13), Today));
    }

    [Fact]
    public void ADayOfAnotherMonthAlsoCarriesTheMonth()
    {
        Assert.Equal("Tue 1 Dec", MailWording.Day(new DateOnly(2026, 12, 1), Today));
    }

    [Fact]
    public void TheSameMonthOfAnotherYearAlsoCarriesTheMonth()
    {
        Assert.Equal("Wed 10 Nov", MailWording.Day(new DateOnly(2027, 11, 10), Today));
    }

    [Fact]
    public void ADateIsWrittenInEnglishWhateverTheLanguageOfTheMachine()
    {
        var previous = (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture);
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");

            Assert.Equal("Fri 13", MailWording.Day(new DateOnly(2026, 11, 13), Today));
        }
        finally
        {
            (Thread.CurrentThread.CurrentCulture, Thread.CurrentThread.CurrentUICulture) = previous;
        }
    }

    [Fact]
    public void ATaskResultSaysTaskForAndItsDay()
    {
        var result = new TreatmentResult(TreatmentKind.Task, new DateOnly(2026, 11, 13), Waiting: false);

        Assert.Equal("Task for Fri 13", MailWording.Result(result, Today));
    }

    [Fact]
    public void ATaskForTodaySaysTaskForToday()
    {
        var result = new TreatmentResult(TreatmentKind.Task, Today, Waiting: false);

        Assert.Equal("Task for today", MailWording.Result(result, Today));
    }

    [Fact]
    public void TheLongestResultCarriesTheMonth()
    {
        var result = new TreatmentResult(TreatmentKind.Task, new DateOnly(2026, 12, 30), Waiting: false);

        Assert.Equal("Task for Wed 30 Dec", MailWording.Result(result, Today));
    }

    [Fact]
    public void ATrashResultSaysMovedToTrash()
    {
        var result = new TreatmentResult(TreatmentKind.Trash, null, Waiting: false);

        Assert.Equal("Moved to trash", MailWording.Result(result, Today));
    }

    [Fact]
    public void TheTaskControlCarriesTheProposedDay()
    {
        Assert.Equal("Add task · Fri 13", MailWording.AddTask(new DateOnly(2026, 11, 13), Today));
        Assert.Equal("Add task", MailWording.AddTask(null, Today));
    }

    [Fact]
    public void TheSummaryOfTheDayCountsTasksTrashAndDismissedLines()
    {
        Assert.Equal(
            "Today · 2 tasks · 2 moved to trash · 3 dismissed",
            MailWording.Summary(new DaySummary(Tasks: 2, MovedToTrash: 2, Dismissed: 3)));
    }

    [Fact]
    public void TheSummaryOfTheDayLeavesOutWhatDidNotHappen()
    {
        Assert.Equal("Today · 1 task", MailWording.Summary(new DaySummary(Tasks: 1, MovedToTrash: 0, Dismissed: 0)));
    }

    [Theory]
    [InlineData(SetAsideReason.Newsletter, "Newsletter")]
    [InlineData(SetAsideReason.Promotion, "Promotion")]
    [InlineData(SetAsideReason.Receipt, "Receipt")]
    [InlineData(SetAsideReason.NoActionNeeded, "No action needed")]
    public void EachReasonForSettingAsideHasItsFramedWord(SetAsideReason reason, string word)
    {
        Assert.Equal(word, MailWording.Label(reason));
    }

    [Theory]
    [InlineData(UnreadableReason.ImagesOnly, "Images only")]
    [InlineData(UnreadableReason.Encrypted, "Encrypted")]
    public void EachUnreadableReasonHasItsFramedLabel(UnreadableReason reason, string label)
    {
        Assert.Equal(label, MailWording.Label(reason));
    }

    [Fact]
    public void AnUnverifiedSenderIsLabelledSenderNotVerified()
    {
        Assert.Equal("Sender not verified", MailWording.SenderNotVerified);
    }
}
