using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// "Ouvrir dans darktable" (spec, Photos: once the sorting is done, darktable
// opens on the folder from here; board 12.4). darktable is looked for each
// time the folder is shown, off the UI thread, and again on the click, so an
// install made meanwhile is found without restarting.
//
// The button stays enabled when darktable is missing: a disabled button says
// nothing of why. Its click then says what to do, in a teaching tip at the
// button, with the way to darktable's install page (Open 33, darktable
// missing). The same tip says when the folder is gone or darktable would not
// start.
internal sealed partial class PhotosView
{
    private static readonly Uri DarktableInstallPage = new("https://www.darktable.org/install/");

    private DarktableInstall? _darktable;

    private void ShowDarktable(bool folderReadable)
    {
        DarktableButton.IsEnabled = folderReadable;
        string tip = AtelierText.Get(_darktable is null ? "Photos_Darktable_TipMissing" : "Photos_Darktable_Tip");
        ToolTipService.SetToolTip(DarktableButton, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(DarktableButton, tip);
    }

    private async void OnDarktableClick(object sender, RoutedEventArgs e)
    {
        if (_place is not { } place) return;
        DarktableTip.IsOpen = false;
        DarktableInstall? known = _darktable;
        DarktableOpenResult result = await Task.Run(() => Darktable.Open(place.Path, known));
        if (result == DarktableOpenResult.Opened) return;

        (string title, string subtitle, bool offerInstall) = result switch
        {
            DarktableOpenResult.NotFound => (AtelierText.Get("Photos_Darktable_NotFound"),
                AtelierText.Get("Photos_Darktable_NotFoundBody"), true),
            DarktableOpenResult.FolderMissing => (AtelierText.Get("Files_NotFound"), "", false),
            _ => (AtelierText.Get("Photos_Darktable_Failed"), AtelierText.Get("Photos_Darktable_FailedBody"), false),
        };
        DarktableTip.Title = title;
        DarktableTip.Subtitle = subtitle;
        DarktableTip.ActionButtonContent = offerInstall ? AtelierText.Get("Photos_Darktable_Install") : null;
        DarktableTip.IsOpen = true;
    }

    private async void OnDarktableTipAction(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        await Windows.System.Launcher.LaunchUriAsync(DarktableInstallPage);
    }
}
