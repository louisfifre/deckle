using Deckle.Lighting.Ambient;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientCaptureRateTests
{
    public static TheoryData<int> AllowedRates()
    {
        var data = new TheoryData<int>();
        foreach (int rate in AmbientCaptureRate.Allowed) data.Add(rate);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedRates))]
    public void AllowedRatePassesThrough(int rate)
        => Assert.Equal(rate, AmbientCaptureRate.Snap(rate));

    [Fact]
    public void RateBetweenTwoChoicesSnapsToTheNearest()
        => Assert.Equal(30, AmbientCaptureRate.Snap(25));

    [Fact]
    public void TieGoesToTheHigherRate()
        => Assert.Equal(60, AmbientCaptureRate.Snap(45));

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(1000)]
    public void NonsenseGivesTheDefault(int rate)
        => Assert.Equal(AmbientCaptureRate.Default, AmbientCaptureRate.Snap(rate));

    [Fact]
    public void NewSettingsUseTheDefaultRate()
        => Assert.Equal(AmbientCaptureRate.Default, new AmbientSettings().CaptureFrameRateHz);
}
