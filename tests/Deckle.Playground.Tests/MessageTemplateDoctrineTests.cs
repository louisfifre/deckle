using Deckle.TestSupport;
using Xunit;

namespace Deckle.Playground.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DecklePlaygroundSource.EntertainmentAreaUsedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.HueCallFailed", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.HueCallFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.LightZoneUpdatedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.MatchEntertainmentArea", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.MicrophonePreviewFellBack", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.NavigationFailed", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.NavigationFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.NavigationRejected", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.NavigationRejectedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.NavTiming", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.PageReady", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.PipelineModeChangedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.ResolveLights", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.ScreenCaptureStartFailed", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.ScreenCaptureStartFailedDetail", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.ScreenCaptureToggled", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.TuningChanged", TemplateDoctrine.PreDoctrine },
        { "DecklePlaygroundSource.ZoneSuggested", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DecklePlaygroundSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
