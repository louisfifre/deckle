using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vad.Tests;

// The VAD model path names the account that downloaded it, so a line carries it
// under %USERPROFILE% and a reader on another machine learns the file, not the person.
[Trait("Category", "observability")]
public sealed class VadPathRedactionTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void TheLoadedDetailNamesTheModelUnderTheProfileVariable()
    {
        using var listener = new TestEventListener("Deckle-Vad");

        DeckleVadSource.Log.SpeechTrimVadLoadedDetail(Path.Combine(Profile, "models", "silero.onnx"));

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimVadLoadedDetail);
        Assert.Equal(@"%USERPROFILE%\models\silero.onnx", ev.Payload![0]);
    }

    [Fact]
    public void TheUnavailableIncidentHidesAPathQuotedByTheFailureText()
    {
        using var listener = new TestEventListener("Deckle-Vad");

        DeckleVadSource.Log.SpeechTrimUnavailable(
            "load_failed", "IOException", $"Could not open '{Profile}\\models\\silero.onnx'");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleVadSource.EvtSpeechTrimUnavailable);
        Assert.Equal(@"Could not open '%USERPROFILE%\models\silero.onnx'", ev.Payload![2]);
    }
}
