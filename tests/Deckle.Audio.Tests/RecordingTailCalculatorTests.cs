using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

[Trait("Category", "unit")]
public class RecordingTailCalculatorTests
{
    [Fact]
    public void TheTailIsActiveWhenTheVoiceRunsUntilStop()
    {
        float[] speaking = Enumerable.Repeat(0.1f, 20).ToArray();
        float[] silent = Enumerable.Repeat(0.0001f, 20).ToArray();

        Assert.Equal("active", RecordingTailCalculator.ComputeTail(speaking)!.Value.TailState);
        Assert.Equal("silent", RecordingTailCalculator.ComputeTail(silent)!.Value.TailState);
    }

    [Fact]
    public void ARecordingTooShortForOneWindowHasNoTail()
    {
        Assert.Null(RecordingTailCalculator.ComputeTail(System.Array.Empty<float>()));
    }
}