using Deckle.TestSupport;
using Xunit;

namespace Deckle.Hud.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleHudSource.CleanupFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.FadeInEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.FadeInStarted", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.HideSyncTimedOut", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.HudDisplaySuppressed", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.OverlayCardChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.ProximityRollup", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.RevealBuildAttemptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.RevealBuildEnded", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.RevealGeometry", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.RevealMaskDegraded", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.RevealMaskRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.SettingsLoadError", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.SettingsLoadWarning", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.StateChanged", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.StrokeCreationAttemptFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.StrokeCreationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.StrokeCreationRecovered", TemplateDoctrine.PreDoctrine },
        { "DeckleHudSource.TranscriptPanelVisibilityChanged", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleHudSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
