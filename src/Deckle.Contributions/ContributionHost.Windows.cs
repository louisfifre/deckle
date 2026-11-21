using System.Diagnostics;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml;
using static Deckle.Contributions.DeckleContributionsSource;

namespace Deckle.Contributions;

public sealed partial class ContributionHost
{
    // The windows alive now, for the theme to reach.
    public IEnumerable<Window> OpenWindows =>
        _slots.Values.Select(s => s.Open).OfType<Window>().ToList();

    public bool IsOpen(string windowName) =>
        _slots.TryGetValue(windowName, out WindowSlot? slot) && slot.Open is not null;

    // Opens the window on a person's request: shown and brought forward, built
    // on first use. False, with the reason in the log, when the window is
    // unknown, developer-only while Developer mode is off, or of a module the
    // person did not install.
    public bool Open(string windowName)
    {
        string? refusal = Refusal(windowName);
        if (refusal is not null)
        {
            Log.WindowNotOpened(windowName, refusal);
            return false;
        }

        WindowSlot slot = _slots[windowName];
        Window window = Ensure(windowName);
        slot.Definition.Show(window);
        _services.WindowOpened(windowName);
        return true;
    }

    // The window created and wired, not shown, whatever the gates say: the
    // self-check shows it without activating it. Built once while it lives.
    public Window Ensure(string windowName)
    {
        if (!_slots.TryGetValue(windowName, out WindowSlot? slot))
            throw new ArgumentException($"No contributed window is named '{windowName}'.", nameof(windowName));
        if (slot.Open is not null) return slot.Open;

        // Not gated: one Stopwatch per window per process, and the provider
        // declares this milestone ungoverned.
        var sw = Stopwatch.StartNew();
        Window window = _services.CreateOrLog(windowName, slot.Definition.Create);
        _services.Place(window, windowName);
        _services.WhenClosed(window, () =>
        {
            if (ReferenceEquals(slot.Open, window)) slot.Open = null;
            slot.Definition.OnClosed?.Invoke(window);
        });

        slot.Open = window;
        slot.Definition.SetRecordingState?.Invoke(window, _lastRecordingState);
        _services.ApplyTheme(window);
        DeckleWindowingSource.Log.WindowLoadComplete(windowName, sw.ElapsedMilliseconds);
        return window;
    }

    // Each open window shows the recording state in its icons, and a window
    // opened later starts from the last state.
    public void SetRecordingState(bool isRecording)
    {
        _lastRecordingState = isRecording;
        foreach (WindowSlot slot in _slots.Values)
        {
            if (slot.Open is { } window) slot.Definition.SetRecordingState?.Invoke(window, isRecording);
        }
    }

    private string? Refusal(string windowName)
    {
        if (!_slots.TryGetValue(windowName, out WindowSlot? slot)) return WindowRefusals.UnknownWindow;
        if (slot.Definition.DeveloperOnly && !_services.DeveloperModeEnabled)
            return WindowRefusals.DeveloperModeOff;
        if (slot.Owner.Descriptor is { } module && !_services.IsModulePresent(module.Id))
            return WindowRefusals.ModuleAbsent;
        return null;
    }
}
