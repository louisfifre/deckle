namespace Deckle.Shell;

// The single question "does Deckle start at logon?", whichever vehicle answers
// it: the HKCU\Run value (AutostartService) or the elevated scheduled task
// (ElevatedStartupService). Each service owns its vehicle and its multi-install
// check; this facade owns the cross-vehicle rule the General page's autostart
// toggle binds to.
//
// The toggle reads ON when either vehicle targets this exe, so turning it OFF
// must remove whichever vehicle is active, or it would read ON again at the
// next Load. The "Start elevated" toggle owns the choice of vehicle; this
// facade never elevates.
public static class StartupService
{
    public static bool DefaultEnabled => AutostartService.DefaultEnabled;

    public static bool StartsAtLogon() =>
        AutostartService.IsEnabled() || ElevatedStartupService.IsEnabled();

    public static bool StartStartup() => AutostartService.Enable();

    // Removes every vehicle and restores none, unlike
    // ElevatedStartupService.Disable(), which converts the task back to the
    // Run key. Removing the task raises one UAC prompt; a declined prompt
    // returns false, which the toggle reverts on.
    public static bool StopStartup()
    {
        bool ok = true;
        if (AutostartService.IsEnabled()) ok &= AutostartService.Disable();
        if (ElevatedStartupService.IsEnabled()) ok &= ElevatedStartupService.RemoveTask();
        return ok;
    }
}
