using Deckle.TestSupport;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleTrackpadSource.ActFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.DragEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.DragStarted", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleTrackpadSource.EngineStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.EngineStopped", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.GesturesNeutralizedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.GestureStateUnreadable", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.GestureWriteFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.InjectionFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.InjectionIncidentEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.PrimaryReleaseFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.RepairCompleted", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.RepairExitUnobserved", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.RepairFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.RepairLaunchFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleTrackpadSource.TapIgnored", TemplateDoctrine.VerboseWithoutPayload },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleTrackpadSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
