using Windows.System;

namespace Deckle.Folders;

// Where "Installer PowerToys…" leads: PowerToys' page in the Microsoft Store,
// where the person decides to install it. Nothing here installs anything.
//
// The Store's own link, ms-windows-store://pdp/?ProductId=, opens a product
// page in the Store app (Microsoft Learn, "Launch the Microsoft Store app").
// XP89DCGQ3K6VLD is PowerToys' Store id, the one Microsoft's
// aka.ms/getPowertoys leads to. A Windows without the Store, such as an LTSC
// edition, has no app for that link: Launcher.QueryUriSupportAsync says so
// beforehand, and Microsoft's install page opens in the browser instead,
// rather than Windows' "look for an app" prompt.
public static class PowerToysInstallPage
{
    public static readonly Uri StoreUri = new("ms-windows-store://pdp/?ProductId=XP89DCGQ3K6VLD");

    public static readonly Uri WebUri = new("https://learn.microsoft.com/windows/powertoys/install");

    // Leaves the app: the self-check never calls it.
    public static async Task OpenAsync()
    {
        string page = "store";
        try
        {
            LaunchQuerySupportStatus store = await Launcher.QueryUriSupportAsync(StoreUri, LaunchQuerySupportType.Uri);
            Uri uri = StoreUri;
            if (store != LaunchQuerySupportStatus.Available)
            {
                page = "web";
                uri = WebUri;
            }
            if (await Launcher.LaunchUriAsync(uri)) DeckleFoldersSource.Log.PowerToysPageOpened(page);
            else DeckleFoldersSource.Log.PowerToysPageNotOpened(page, "LaunchRefused");
        }
        catch (Exception ex)
        {
            DeckleFoldersSource.Log.PowerToysPageNotOpened(page, ex.GetType().Name);
        }
    }
}
