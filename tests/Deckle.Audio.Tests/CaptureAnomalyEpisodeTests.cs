using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

[Trait("Category", "unit")]
public sealed class CaptureAnomalyEpisodeTests
{
    [Fact]
    public void RepeatedFailuresOpenOnlyOneEpisode()
    {
        CaptureAnomalyEpisode episode = default;

        Assert.Equal(CaptureAnomalyTransition.Opened, episode.ObserveFailure());
        Assert.Equal(CaptureAnomalyTransition.None, episode.ObserveFailure());
        Assert.Equal(CaptureAnomalyTransition.None, episode.ObserveFailure());
        Assert.Equal(3, episode.Occurrences);
        Assert.False(episode.Recovered);
    }

    [Fact]
    public void FirstSuccessClosesEpisodeWithoutAllowingAReopen()
    {
        CaptureAnomalyEpisode episode = default;

        episode.ObserveFailure();
        Assert.Equal(CaptureAnomalyTransition.Recovered, episode.ObserveSuccess());
        Assert.Equal(CaptureAnomalyTransition.None, episode.ObserveSuccess());
        Assert.Equal(CaptureAnomalyTransition.None, episode.ObserveFailure());
        Assert.True(episode.Recovered);
        Assert.Equal(2, episode.Occurrences);
    }
}
