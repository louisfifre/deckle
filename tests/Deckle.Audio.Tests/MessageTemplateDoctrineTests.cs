using Deckle.TestSupport;
using Xunit;

namespace Deckle.Audio.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleAudioSource.AudioDataRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.AudioFileDecodeCrashed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.AudioFileDecodedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.AudioFileDecodeFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.AudioFileDecodeFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.AudioFileDecodeStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureDrainDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureLagDetected", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureLagDetectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureReleaseDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureReleaseFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureReleaseRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureSettingsResolved", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.CaptureStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.DurationCapReachedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.EmptyBufferReceivedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.LowAudioDetectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.MicrophoneOpenFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.MicrophoneStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.MicrophoneTelemetryEmpty", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleAudioSource.MicrophoneTelemetryRecorded", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.NoAudioDataDetected", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.PlaybackCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.PlaybackFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.PlaybackSkipped", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.PlaybackStalled", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.PlaybackStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingAnomaliesSummary", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingTailSummary", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.RecordingTailSummaryDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.SettingsLoadComplete", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.SettingsLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleAudioSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleAudioSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
