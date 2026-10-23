using Deckle.TestSupport;
using Xunit;

namespace Deckle.Shell.TrayMenu.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleShellTrayMenuSource.ActivationMovedToOwnedPopup", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.CarrierStyleFinalized", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.Disposed", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.FlyoutBuilt", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.FlyoutClosed", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.FlyoutShownAt", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.FlyoutShownAtDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.FrameLoaded", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.Hidden", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.HiddenDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.HostConstructed", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.HostConstructedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.ItemClicked", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.ItemClickedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.ShowIgnoredAfterDismiss", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.ShowIgnoredAfterDismissDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleShellTrayMenuSource.ShowRequested", TemplateDoctrine.VerboseWithoutPayload },
        { "DeckleShellTrayMenuSource.WindowActivated", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleShellTrayMenuSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
