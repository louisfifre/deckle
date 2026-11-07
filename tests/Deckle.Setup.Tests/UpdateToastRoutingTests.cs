using Deckle.Notifications;
using Xunit;

namespace Deckle.Setup.Tests;

// A press anywhere on the toast raises an activation, so the body click must
// do no more than the "View update" button: open the update window, where
// installing is its own explicit choice. Only "Later" and a dismissed toast
// leave the offer parked.
[Trait("Category", "unit")]
public sealed class UpdateToastRoutingTests
{
    [Fact]
    public void TheViewButtonOpensTheUpdateWindow()
    {
        Assert.True(SetupNotifications.ShouldOpenUpdateWindow(
            new NotificationResponse(SetupNotifications.ViewActionId, null)));
    }

    [Fact]
    public void ABodyClickOpensTheUpdateWindowAndNothingMore()
    {
        Assert.True(SetupNotifications.ShouldOpenUpdateWindow(
            new NotificationResponse(NotificationResponse.BodyActionId, null)));
    }

    [Theory]
    [InlineData("later")]
    [InlineData("install")]
    public void AnyOtherActionLeavesTheOfferParked(string actionId)
    {
        Assert.False(SetupNotifications.ShouldOpenUpdateWindow(new NotificationResponse(actionId, null)));
    }

    [Fact]
    public void ADismissedToastLeavesTheOfferParked()
    {
        Assert.False(SetupNotifications.ShouldOpenUpdateWindow(null));
    }

    [Fact]
    public void TheToastOffersViewAndLaterOnly()
    {
        Assert.Equal(
            new[] { SetupNotifications.ViewActionId, "later" },
            SetupNotifications.UpdateAvailable.Actions!.Select(a => a.Id));
    }
}
