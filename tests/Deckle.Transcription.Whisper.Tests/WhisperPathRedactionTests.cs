using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// A Whisper line is read on another machine, so a model or runtime path reaches it
// under %USERPROFILE%, never as C:\Users\<name>\...
[Collection(OperationalObservabilityCollection.Name)]
[Trait("Category", "observability")]
public sealed class WhisperPathRedactionTests : IDisposable
{
    private const string Provider = "Deckle-Whisper";

    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public WhisperPathRedactionTests()
        => OperationalLogAdmission.Configure(
            static activity => activity == OperationalLogActivity.Transcription);

    public void Dispose()
    {
        OperationalLogAdmission.SetActive(OperationalLogActivity.Transcription, false);
        OperationalLogAdmission.Configure(static _ => false);
    }

    [Fact]
    public void ModelLoadDetailNamesThePathUnderTheProfileVariable()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhisperSource.Log.ModelLoadStartingDetail(
            Path.Combine(Profile, "models", "ggml.bin"), 142.5, 0, true, true);

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleWhisperSource.EvtModelLoadStartingDetail);
        Assert.Equal(@"%USERPROFILE%\models\ggml.bin", Payload(ev, "path"));
    }

    [Fact]
    public void NativeRuntimeDetailNamesThePathUnderTheProfileVariable()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhisperSource.Log.NativeRuntimeLoadedDetail(
            Path.Combine(Profile, "native", "whisper.dll"), 1024, "1.7.0", "2025-01-01");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleWhisperSource.EvtNativeRuntimeLoadedDetail);
        Assert.Equal(@"%USERPROFILE%\native\whisper.dll", Payload(ev, "path"));
    }

    [Fact]
    public void ANativeWarningThatQuotesAPathHidesTheProfile()
    {
        using var listener = new TestEventListener(Provider);

        DeckleWhisperSource.Log.NativeLogWarning($"failed to open {Profile}\\models\\ggml.bin");

        var ev = Assert.Single(listener.Events, e => e.EventId == DeckleWhisperSource.EvtNativeLogWarning);
        Assert.Equal(@"failed to open %USERPROFILE%\models\ggml.bin", Payload(ev, "message"));
    }

    private static object? Payload(System.Diagnostics.Tracing.EventWrittenEventArgs e, string name)
        => e.Payload![e.PayloadNames!.IndexOf(name)];
}
