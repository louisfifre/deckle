using System.Globalization;
using Xunit;

namespace Deckle.Folders.Tests;

// When the last analysis ran, as Refresh's tooltip words it: the time today,
// the date any other day.
[Trait("Category", "unit")]
public sealed class SizeAnalysisMomentTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    [Fact]
    public void AnAnalysisOfTodayReadsAsItsTime()
    {
        var moment = SizeAnalysisMoment.Of(new DateTime(2026, 11, 15, 9, 5, 0), new DateTime(2026, 11, 15, 23, 59, 0), French);

        Assert.Equal(new SizeAnalysisMoment(true, "09:05"), moment);
    }

    [Fact]
    public void AnAnalysisOfAnEarlierDayReadsAsItsDateEvenLessThanADayAgo()
    {
        var moment = SizeAnalysisMoment.Of(new DateTime(2026, 11, 14, 23, 50, 0), new DateTime(2026, 11, 15, 0, 10, 0), French);

        Assert.Equal(new SizeAnalysisMoment(false, "14/11/2026"), moment);
    }
}
