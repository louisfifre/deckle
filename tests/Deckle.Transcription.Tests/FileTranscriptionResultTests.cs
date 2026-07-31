using Deckle.Transcription;
using Xunit;

namespace Deckle.Transcription.Tests;

[Trait("Category", "regression")]
public sealed class FileTranscriptionResultTests
{
    [Fact]
    public void RepetitionAbortIsNeverAcceptedAsAFileTranscript()
    {
        var result = new TranscriptionResult(
            [],
            ".NET, Visual Studio, Python, Whisper, le shell.",
            1,
            0,
            Aborted: true,
            ResultCode: 0);

        Assert.False(TranscriptionEngine.IsFileTranscriptionResultUsable(result));
    }

    [Fact]
    public void SuccessfulCompleteResultIsAccepted()
    {
        var result = new TranscriptionResult([], "Bonjour", 1, 0, Aborted: false, ResultCode: 0);

        Assert.True(TranscriptionEngine.IsFileTranscriptionResultUsable(result));
    }

    [Fact]
    public void BackendFailureIsNotAccepted()
    {
        var result = new TranscriptionResult([], "partial", 1, 0, Aborted: false, ResultCode: -1);

        Assert.False(TranscriptionEngine.IsFileTranscriptionResultUsable(result));
    }
}
