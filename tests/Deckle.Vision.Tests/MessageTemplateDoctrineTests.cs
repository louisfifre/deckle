using Deckle.TestSupport;
using Xunit;

namespace Deckle.Vision.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleVisionSource.AccessLostRecovering", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleVisionSource.AcquireFrameFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.AcquireFrameIncident", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.AcquireFrameRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureAccessIncident", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureAccessRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureCleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureFormatRenegotiatedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureIncidentAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureLoopCrashed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureLoopEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureLoopStopTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureLoopWaitFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureSessionConfigured", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.CaptureStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DeviceLost", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationRecreateAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationRecreateAttemptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationRecreateAttemptFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationRecreated", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationRecreateEpisodeDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.DuplicationResizeDetected", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.FrameConsumerThrewDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.FrameOwnershipRecoveryAbandoned", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.HdrDetectionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.Heartbeat", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.MonitorNotFound", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.ReleaseFrameNonZero", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.SamplerInitialized", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.SamplerMapFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.SamplerProcessFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.ScreenCaptureStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.ScreenCaptureStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.ScreenCaptureStoppedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.SecureDesktopRecovering", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.SubscriberThrew", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.TargetMonitorResolved", TemplateDoctrine.PreDoctrine },
        { "DeckleVisionSource.TextureQueryFailedDetail", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleVisionSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
