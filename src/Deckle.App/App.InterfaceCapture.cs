using Deckle.Settings;
using Deckle.Vision;
using WinRT.Interop;

namespace Deckle.App;

// ── Developer mode: the interface capture ─────────────────────────────────────
//
// Captures every open Deckle window to a PNG, then each settings page in turn,
// into <UserDataRoot>\diagnostics\interface-captures\<date>_<time>\ with an
// index.json, so a review agent can judge the interface from files without
// driving it. Started from Settings > General > Developer > Capture the
// interface, shown only while Developer mode is on.
//
// The App is the one place that knows which window is which, so it names them:
// the Log window "log", the Playground "playground", the HUD "hud". Any other
// visible window of this process (the transcript panel, a rewrite offer, the
// setup wizard, a taskbar cover) is captured too, named after its title. The
// Settings window is captured once per page, by its own walk, rather than as
// it happens to stand. How each image is taken, and why that way, is written
// at the top of Deckle.Vision's WindowSnapshot.
//
// Only visible windows are captured: a hidden HUD between two takes has
// nothing on screen. To capture the HUD, start a dictation and run the capture
// while it shows (a script trigger would make that practical; none exists yet).
public partial class App
{
    private bool _interfaceCaptureRunning;

    // Never throws: a failure of the run is logged as its end, and the outcome
    // says what was written. Null when developer mode is off, or when a capture
    // is already running.
    private async Task<InterfaceCaptureOutcome?> CaptureInterfaceAsync()
    {
        if (!SettingsService.Instance.Current.Developer.DeveloperMode || _interfaceCaptureRunning)
            return null;

        _interfaceCaptureRunning = true;
        InterfaceCaptureRun? run = null;
        try
        {
            run = InterfaceCaptureRun.Begin(DateTimeOffset.Now);
        }
        catch (Exception)
        {
            // Begin logged the cause and closed the run.
            _interfaceCaptureRunning = false;
            return new InterfaceCaptureOutcome(InterfaceCaptureRun.CapturesDirectory, 0, 1);
        }

        string step = "capture_windows";
        try
        {
            nint settingsHwnd = _settingsWindow is null ? 0 : WindowNative.GetWindowHandle(_settingsWindow);
            Dictionary<nint, string> known = NamedWindows();

            foreach (nint hwnd in InterfaceCaptureRun.VisibleWindows())
            {
                if (hwnd == settingsHwnd) continue;
                string name = known.TryGetValue(hwnd, out string? n) ? n : InterfaceCaptureRun.TitleOf(hwnd);
                await run.CaptureAsync(hwnd, name);
            }

            if (_settingsWindow is { } settings && settingsHwnd != 0)
            {
                step = "walk_settings_pages";
                await settings.CaptureEachPageAsync((pageTag, label) =>
                    run.CaptureAsync(settingsHwnd, "settings", InterfaceCaptureNames.PageSlug(pageTag), label));
            }

            run.Complete();
        }
        catch (Exception ex)
        {
            run.Stop(step, ex);
        }
        finally
        {
            _interfaceCaptureRunning = false;
        }

        return new InterfaceCaptureOutcome(run.Directory, run.Captured, run.Failed);
    }

    // The windows the App holds, by the stable name their images take.
    private Dictionary<nint, string> NamedWindows()
    {
        var names = new Dictionary<nint, string>();
        void Add(Microsoft.UI.Xaml.Window? window, string name)
        {
            if (window is not null) names[WindowNative.GetWindowHandle(window)] = name;
        }
        Add(_logWindow, "log");
        Add(_playgroundWindow, "playground");
        Add(_hudWindow, "hud");
        return names;
    }
}
