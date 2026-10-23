using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleInputSource.FrameRollup", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.HostStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.HostStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.HostStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.HostStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.HostStopTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.InputPumpFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.InputRelayDegraded", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.InputRelayRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.KeyboardHostStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.KeyboardHostStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.KeyboardHostStartFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.KeyboardHostStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.KeyboardRollup", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.MouseHookFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.MouseHookSilenceEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.MouseHookSilenceStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.ParserCreateFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.RecordingFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.RecordingSizeUnavailable", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.RecordingStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.RecordingStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.RecordingStoppedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.TouchpadDetectedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.TouchpadEnumerationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.WheelDeviceObserved", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.WheelRecordingFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.WheelRecordingStartedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.WheelRecordingStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleInputSource.WheelRecordingStoppedDetail", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleInputSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
