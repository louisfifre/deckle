using Deckle.TestSupport;
using Xunit;

namespace Deckle.Notifications.Tests;

// Every template of this suite's providers follows the shape of its level, as
// TemplateDoctrine states it. The list names the templates that do not yet, each
// with its reason; an entry that outlives its breach fails the test too, so the
// list only shrinks.
[Trait("Category", "observability")]
public sealed class MessageTemplateDoctrineTests
{
    private static readonly Dictionary<string, string> KnownBreaches = new(StringComparer.Ordinal)
    {
        { "DeckleNotificationsSource.CatalogRegistered", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.DispatcherInitializedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationDropped", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationResponded", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationRespondedDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationShown", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationShownDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationUnanswered", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.NotificationUnansweredDetail", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.PromptCancelled", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.ToastRegistrationFailed", TemplateDoctrine.PreDoctrine },
        { "DeckleNotificationsSource.ToastWithdrawFailed", TemplateDoctrine.PreDoctrine },
    };

    [Fact]
    public void EveryTemplateFollowsTheShapeOfItsLevel()
    {
        IReadOnlyList<string> failures = TemplateDoctrine.Violations([typeof(DeckleNotificationsSource)], KnownBreaches);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
