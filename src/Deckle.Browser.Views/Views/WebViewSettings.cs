using Microsoft.Web.WebView2.Core;

namespace Deckle.Browser.Views;

// The security settings of every CoreWebView2 the window creates, in one place
// so no web view is made without them.
internal static class WebViewSettings
{
    /// <summary>Applies the settings to a web view's core. Returns whether strict
    /// tracking prevention is in force (read back after it is set).</summary>
    public static bool Apply(CoreWebView2 core)
    {
        CoreWebView2Settings settings = core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        // The browser's own accelerator keys (Ctrl+F, F5, Ctrl+W...) would be
        // taken by the page's process: off, the window's own key handling
        // (BrowserWindow.Shortcuts) runs the shortcuts, with the page focused too.
        settings.AreBrowserAcceleratorKeysEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        try
        {
            core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;
            return core.Profile.PreferredTrackingPreventionLevel == CoreWebView2TrackingPreventionLevel.Strict;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException
                                       or NotImplementedException or ArgumentException)
        {
            DeckleBrowserViewsSource.Log.TrackingPreventionFailed(ex.GetType().Name);
            return false;
        }
    }
}
