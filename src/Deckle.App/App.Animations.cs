using Deckle.Diagnostics;
using Deckle.Hud;
using Deckle.Notifications;

namespace Deckle.App;

// The one-time offer of Deckle's own animations (ADR-0009). Under the default
// "Same as Windows", a person whose Windows animations are off gets a HUD with
// no processing outline without having chosen it. One toast tells them and
// offers to turn Deckle's animations on, leaving Windows as it is. It replaces
// a step in the setup wizard: the wizard's first process cannot write the
// settings yet, and its second shows no page when nothing is left to download.
public partial class App
{
    // Resident app only, after the notification catalogue is registered.
    private static void OfferOwnAnimations()
    {
        var settings = Settings.SettingsService.Instance;
        var appearance = settings.Current.Appearance;
        if (!AnimationPreference.ShouldOfferOwnAnimations(
                appearance.Animations,
                AnimationPreference.Instance.WindowsAnimationsEnabled,
                appearance.AnimationsChoiceOffered))
        {
            return;
        }

        // A toast that cannot show now (no channel, notifications off) is
        // not an offer made: the next launch tries again.
        if (NotificationDispatcher.Instance is not { } dispatcher
            || !dispatcher.CanPrompt(AnimationNotifications.OfferOwnAnimations))
        {
            return;
        }

        // Recorded before the toast shows: "once" means once shown, answered
        // or not, so an ignored toast never comes back.
        appearance.AnimationsChoiceOffered = true;
        settings.Save();

        _ = Task.Run(async () =>
        {
            NotificationResponse? response;
            try
            {
                response = await dispatcher.PromptAsync(AnimationNotifications.OfferOwnAnimations)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The dispatcher has already logged the failure
                // (NotificationFailed); the setting stays as it was.
                return;
            }

            if (!AnimationNotifications.ShouldTurnOn(response)) return;

            // AnimationPreference picks the new value up from
            // SettingsService.Changed, within the save debounce.
            settings.Current.Appearance.Animations = AnimationPreference.ModeOn;
            settings.Save();
            DeckleSettingsUxSource.Log.SettingChanged("Appearance.Animations", AnimationPreference.ModeOn);
        });
    }
}
