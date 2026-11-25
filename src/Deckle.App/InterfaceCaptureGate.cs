using Deckle.Settings;

namespace Deckle.App;

// The one gate in front of the interface capture (App.InterfaceCapture.cs).
//
// What it guarantees: the capture exists only while Developer mode is on. Both
// doors, Settings > General > Developer > Capture the interface and the
// developer tray menu, pass through here; the mode is read at each start,
// never cached, so turning it off closes both doors at once. With the mode off
// nothing is called: no Settings window opens, no capture runs, no folder
// opens. Only one capture runs at a time; a start while one runs is refused.
//
// Why: Louis wants the capture to exist only in Developer mode, robustly, so
// the rule lives in one small tested place instead of inside the capture.
//
// The self-check (App.SelfCheck.cs) takes its own images through its own
// pipeline, in its own data root, and does not pass through this gate.
internal sealed class InterfaceCaptureGate
{
    private readonly Func<bool> _developerModeOn;
    private readonly Func<Task<InterfaceCaptureOutcome>> _run;
    private readonly Action _openSettings;
    private readonly Action<string> _openFolder;
    private bool _running;

    public InterfaceCaptureGate(
        Func<bool> developerModeOn,
        Func<Task<InterfaceCaptureOutcome>> run,
        Action openSettings,
        Action<string> openFolder)
    {
        _developerModeOn = developerModeOn;
        _run = run;
        _openSettings = openSettings;
        _openFolder = openFolder;
    }

    // The Settings card. Null when the mode is off or a capture is running.
    public async Task<InterfaceCaptureOutcome?> FromSettingsAsync()
    {
        if (!_developerModeOn() || _running) return null;
        return await RunAsync();
    }

    // The developer tray menu. Settings opens first so its pages are part of
    // the run; the folder opens at the end, only if something was captured.
    public async Task FromTrayAsync()
    {
        if (!_developerModeOn() || _running) return;
        _openSettings();
        InterfaceCaptureOutcome outcome = await RunAsync();
        if (outcome.Captured > 0) _openFolder(outcome.Folder);
    }

    private async Task<InterfaceCaptureOutcome> RunAsync()
    {
        _running = true;
        try
        {
            return await _run();
        }
        finally
        {
            _running = false;
        }
    }
}
