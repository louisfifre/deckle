namespace Deckle.Shell;

// ── StartupService ───────────────────────────────────────────────────────────
//
// The facade for the single question "does Deckle start at logon?", regardless
// of which vehicle answers it. Two mutually-exclusive vehicles can carry that:
// the HKCU\Run value (AutostartService) and the elevated scheduled task
// (ElevatedStartupService). Each service owns its own vehicle and its own
// multi-install ownership check; this facade owns only the cross-vehicle rule
// the General page's autostart toggle binds to.
//
// Why it exists: the autostart toggle means "start at logon" (the *whether*),
// not "via the Run key". Probing a single vehicle made the toggle read OFF
// while the elevated task was the active vehicle — it lied. And making the
// probe honest (OR over both vehicles) forces the write side to match: turning
// the toggle OFF must remove whichever vehicle is active, or the toggle would
// re-read ON on the next Load and the user's "off" would do nothing.
//
// The "Start elevated" toggle (Trackpad page) stays the owner of the *how* —
// it converts the active vehicle between Run key and elevated task. This facade
// never elevates; it only starts the default vehicle or stops everything.
public static class StartupService
{
    // "Not registered" is the conceptual default — off. Delegates to the Run-key
    // vehicle's own default so the literal `false` lives in exactly one place.
    public static bool DefaultEnabled => AutostartService.DefaultEnabled;

    // True if Deckle starts at logon by *either* vehicle targeting this exe. The
    // honest reading the autostart toggle shows.
    public static bool StartsAtLogon() =>
        AutostartService.IsEnabled() || ElevatedStartupService.IsEnabled();

    // Begin starting at logon via the default, non-elevated vehicle (the Run
    // key). Upgrading to the elevated task stays the "Start elevated" toggle's
    // job — this facade never raises the run level on its own.
    public static bool StartStartup() => AutostartService.Enable();

    // Stop starting at logon, whatever the vehicle. Removes every vehicle that
    // currently targets this exe and restores none — "off means off". This is
    // the deliberate contrast with ElevatedStartupService.Disable(), which
    // *converts* the elevated task back to the Run key (its meaning is "stop
    // being elevated", not "stop starting"). Removing the elevated task needs
    // elevation, so this raises one UAC prompt when that vehicle is active; a
    // declined prompt surfaces as a false return, which the toggle reverts on.
    public static bool StopStartup()
    {
        bool ok = true;
        if (AutostartService.IsEnabled()) ok &= AutostartService.Disable();
        if (ElevatedStartupService.IsEnabled()) ok &= ElevatedStartupService.RemoveTask();
        return ok;
    }
}
