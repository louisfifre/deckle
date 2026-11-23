using Deckle.Orchestration;
using Xunit;

namespace Deckle.Orchestration.Tests;

[Trait("Category", "unit")]
public sealed class DetectionStatusTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void AppsAreNoticedOnlyAtTheForegroundWhenTheProcessTraceIsMissing(
        bool processTrace, bool foregroundMonitor, bool expected)
    {
        var status = new DetectionStatus(processTrace, foregroundMonitor);

        Assert.Equal(expected, status.NoticesOnlyAtForeground);
    }
}
