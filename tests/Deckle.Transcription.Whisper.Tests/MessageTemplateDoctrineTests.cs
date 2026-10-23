using Deckle.TestSupport;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleWhisperSource.LogHookLineFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.LogHookLineFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.LogHookUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelDeviceUnknown", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelFileProbeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelGpuUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelLoadFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelLoadFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelLoadStarting", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelLoadStartingDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.ModelOnGpu", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeLogError", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeLogLine", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeLogWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeCleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeCopied", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeCopiedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeLoadedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeProbeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeProvisionEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeProvisionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeProvisionFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeProvisionStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.NativeRuntimeSystemInfo", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.SegmentCallbackFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.SegmentCallbackFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleWhisperSource.TranscribeFailed", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleWhisperSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
