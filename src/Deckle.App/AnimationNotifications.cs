using System.Collections.Generic;
using Deckle.Notifications;

namespace Deckle.App;

// The one-time offer of Deckle's own animations to a person whose Windows
// animations are off (App.Animations.cs, ADR-0009). Lives in the App so
// Deckle.Hud stays free of any Notifications dependency. The descriptor Id
// is a stable public contract (point.snake_case, never renamed or
// recycled); the strings are .resw keys mirrored in Deckle.App's
// Resources.resw.
public static class AnimationNotifications
{
    // Echoed back in NotificationResponse.ActionId.
    public const string TurnOnAction = "turn_on";
    public const string LeaveOffAction = "leave_off";

    public static readonly NotificationDescriptor OfferOwnAnimations = new(
        Id: "appearance.offer_animations",
        Category: "appearance",
        TitleKey: "AnimationNotifications_Offer_Title",
        BodyKey: "AnimationNotifications_Offer_Body",
        Severity: NotificationSeverity.Info,
        Channel: NotificationChannel.Toast,
        Actions: new[]
        {
            new NotificationAction(TurnOnAction, "AnimationNotifications_Offer_TurnOn"),
            new NotificationAction(LeaveOffAction, "AnimationNotifications_Offer_LeaveOff"),
        });

    // Only the explicit button turns the animations on. Windows raises the
    // body action for any press on the toast, a stray-click risk, so the
    // body changes nothing.
    public static bool ShouldTurnOn(NotificationResponse? response) =>
        response is { ActionId: TurnOnAction };

    public static IReadOnlyList<NotificationDescriptor> All { get; } = new[] { OfferOwnAnimations };
}
