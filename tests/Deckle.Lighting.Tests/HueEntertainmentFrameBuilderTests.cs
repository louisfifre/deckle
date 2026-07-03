using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueEntertainmentFrameBuilderTests
{
    [Fact]
    public void BuildFramesWritesTheHueStreamHeaderAndRgbPayload()
    {
        var frames = HueEntertainmentFrameBuilder.BuildFrames(
            "00112233-4455-6677-8899-AABBCCDDEEFF",
            [
                new HueEntertainmentChannelColor(1, new LightColor(0x10, 0x20, 0x30)),
                new HueEntertainmentChannelColor(2, new LightColor(0xA0, 0xB0, 0xC0)),
            ],
            sequence: 7);

        Assert.Single(frames);
        byte[] frame = frames[0];

        Assert.Equal("HueStream"u8.ToArray(), frame[..9]);
        Assert.Equal(
            new byte[] { 0x02, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00 },
            frame[9..16]);
        Assert.Equal(
            "00112233-4455-6677-8899-aabbccddeeff"u8.ToArray(),
            frame[16..52]);
        Assert.Equal(
            new byte[]
            {
                0x01, 0x10, 0x10, 0x20, 0x20, 0x30, 0x30,
                0x02, 0xA0, 0xA0, 0xB0, 0xB0, 0xC0, 0xC0,
            },
            frame[52..]);
    }

    [Fact]
    public void BuildFramesSplitsAfterTwentyChannels()
    {
        var colors = Enumerable.Range(1, 21)
            .Select(i => new HueEntertainmentChannelColor(i, new LightColor((byte)i, 0, 0)))
            .ToArray();

        var frames = HueEntertainmentFrameBuilder.BuildFrames(
            "00112233-4455-6677-8899-aabbccddeeff",
            colors);

        Assert.Equal(2, frames.Count);
        Assert.Equal(52 + 20 * 7, frames[0].Length);
        Assert.Equal(52 + 1 * 7, frames[1].Length);
        Assert.Equal(1, frames[0][11]);
        Assert.Equal(2, frames[1][11]);
        Assert.Equal(1, frames[0][52]);
        Assert.Equal(20, frames[0][52 + 19 * 7]);
        Assert.Equal(21, frames[1][52]);
    }

    [Fact]
    public void BuildFramesRejectsAChannelIdThatDoesNotFitOneByte()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HueEntertainmentFrameBuilder.BuildFrames(
                "00112233-4455-6677-8899-aabbccddeeff",
                [new HueEntertainmentChannelColor(256, LightColor.Black)]));
    }
}
