using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "unit")]
public sealed class TranscriptionDeliveryTests
{
    [Fact]
    public void AdjacentFileCarriesItsOwnSourcePath()
    {
        TranscriptionDelivery delivery =
            TranscriptionDelivery.AdjacentFile(@"D:\audio\meeting.m4a");

        Assert.True(delivery.IsFile);
        Assert.Equal(TranscriptionDeliveryKind.AdjacentFile, delivery.Kind);
        Assert.Equal(@"D:\audio\meeting.m4a", delivery.SourceAudioPath);
    }

    [Fact]
    public void DictationHasNoFileDestination()
    {
        TranscriptionDelivery delivery = TranscriptionDelivery.Dictation;

        Assert.False(delivery.IsFile);
        Assert.Null(delivery.SourceAudioPath);
    }
}
