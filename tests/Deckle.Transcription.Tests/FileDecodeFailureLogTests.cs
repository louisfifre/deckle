using Deckle.Audio;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Tests;

// A dropped audio file's name can say who the person is or what they discuss.
// The default log keeps the reason and the name stays in a Verbose detail event.
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class FileDecodeFailureLogTests : IDisposable
{
    private const string SecretName = "private-memo-example.m4a";
    private static readonly string SecretPath = Path.Combine(Path.GetTempPath(), "folder", SecretName);

    public FileDecodeFailureLogTests()
        => OperationalLogAdmission.Configure(static _ => false);

    public void Dispose()
        => OperationalLogAdmission.Configure(static _ => false);

    [Fact]
    public void DefaultLogCarriesNoFileNameWhenADecodeFails()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        TranscriptionEngine.HandleDecodeFailure(
            SecretPath,
            new AudioFileDecodeResult(AudioFileDecodeStatus.UnsupportedFormat, default, 0));

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleWhispSource.EvtFileDecodeFailed);
        Assert.DoesNotContain(ev.Payload!, v => v is string s && s.Contains(SecretName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("UnsupportedFormat", ev.Payload![0]);
    }

    [Fact]
    public void DefaultLogMasksTheFileNameInADecodeExceptionMessage()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        TranscriptionEngine.HandleDecodeException(
            SecretPath,
            new IOException($"Could not read '{SecretPath}' ({SecretName})"));

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleWhispSource.EvtFileDecodeFailed);
        Assert.DoesNotContain(ev.Payload!, v => v is string s && s.Contains(SecretName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("IOException", ev.Payload![1]);
    }

    [Fact]
    public void TheNameReachesTheLogOnlyInTheFilteredVerboseDetail()
    {
        OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);
        using var listener = new TestEventListener("Deckle-Whisp");

        TranscriptionEngine.HandleDecodeFailure(
            SecretPath,
            new AudioFileDecodeResult(AudioFileDecodeStatus.NoAudioTrack, default, 0));

        var withName = Assert.Single(
            listener.Events,
            e => e.Payload!.Any(v => v is string s && s.Contains(SecretName)));
        Assert.Equal(DeckleWhispSource.EvtFileDecodeFailedDetail, withName.EventId);
    }

    [Fact]
    public void AnEmptyFileCountsWithoutItsNameInTheDefaultLog()
    {
        using var listener = new TestEventListener("Deckle-Whisp");

        DeckleWhispSource.Log.FileTranscriptionEmpty(1, 2);
        DeckleWhispSource.Log.FileTranscriptionEmptyDetail(1, SecretName);

        var ev = Assert.Single(listener.Events);
        Assert.Equal(DeckleWhispSource.EvtFileTranscriptionEmpty, ev.EventId);
        Assert.DoesNotContain(ev.Payload!, v => v is string);
    }
}
