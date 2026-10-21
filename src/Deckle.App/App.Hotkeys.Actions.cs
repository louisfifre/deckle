using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;
using Deckle.Settings;

namespace Deckle.App;

// Id is the RegisterHotKey id that WM_HOTKEY hands back to OnHotkey. ToggleId is
// the TrayToggle id ("record" for the dictation action).
internal sealed record HotkeyAction(
    int Id, string ToggleId, string Label, Func<HotkeyChord?> Chord, Action Invoke);

// Toggle actions invoke the panel's own TrayToggle delegate, so the panel and
// the hotkeys cannot diverge.
public partial class App
{
    private const int RecordHotkeyId = 1;

    // Ids stay stable: they appear in logs. A toggle missing here gets no hotkey.
    private static readonly Dictionary<string, (int Id, Func<HotkeysSettings, HotkeyChord?> Chord)> ToggleHotkeys = new()
    {
        ["precision-scroll"] = (2, h => h.PrecisionScroll),
        ["trackpad"]         = (3, h => h.Trackpad),
        ["taskbar-cover"]    = (4, h => h.TaskbarCover),
        ["autocorrect"]      = (5, h => h.Autocorrect),
        ["rewrite"]          = (6, h => h.Rewrite),
        ["ambient"]          = (7, h => h.AmbientLight),
    };

    private IReadOnlyList<HotkeyAction> _hotkeyActions = [];

    // False when the WM_HOTKEY subclass could not be installed: a rebind
    // would then acquire chords nobody listens to.
    private bool _hotkeysInstalled;

    private IReadOnlyList<HotkeyAction> BuildHotkeyActions(
        StartupContext context, IReadOnlyList<TrayToggle> toggles)
    {
        var actions = new List<HotkeyAction>();

        if (context.TranscriptionPresent)
        {
            actions.Add(new(
                RecordHotkeyId,
                "record",
                Loc.Get("TrayPanel_Record"),
                () => SettingsService.Instance.Current.Hotkeys.Record,
                ToggleRecordingFromHotkey));
        }

        foreach (TrayToggle toggle in toggles)
        {
            if (!ToggleHotkeys.TryGetValue(toggle.Id, out var hotkey)) continue;
            actions.Add(new(
                hotkey.Id,
                toggle.Id,
                toggle.Label,
                () => hotkey.Chord(SettingsService.Instance.Current.Hotkeys),
                () =>
                {
                    DeckleAppSource.Log.HotkeyToggle();
                    DeckleAppSource.Log.HotkeyToggleDetail(toggle.Id);
                    toggle.Toggle();
                }));
        }

        return actions;
    }

    // MOD_NOREPEAT keeps a held chord from firing again on keyboard auto-repeat.
    private IReadOnlyList<GlobalHotkeyBinding> BuildHotkeyBindings()
    {
        var bindings = new List<GlobalHotkeyBinding>();
        foreach (HotkeyAction action in _hotkeyActions)
        {
            if (action.Chord() is not { IsComplete: true } chord) continue;
            bindings.Add(new(
                action.Id,
                (uint)chord.Modifiers | Core.NativeMethods.MOD_NOREPEAT,
                chord.ScanCode));
        }
        return bindings;
    }

    // The chord as Settings shows it ("Ctrl + Shift + `"), so a log line
    // matches what the person sees on the page.
    private string HotkeyChordLabel(int hotkeyId)
        => HotkeyChordText.Format(_hotkeyActions.FirstOrDefault(a => a.Id == hotkeyId)?.Chord())
           ?? "unbound";

    private void ReportHotkeyFailures(IReadOnlyList<HotkeyRegistrationFailure> failures)
    {
        foreach (HotkeyRegistrationFailure failure in failures)
        {
            HotkeyAction? action = _hotkeyActions.FirstOrDefault(a => a.Id == failure.Id);
            string label = action?.Label ?? $"id={failure.Id}";
            GlobalHotkeyBinding binding = _hotkeyManager?.Bindings.FirstOrDefault(b => b.Id == failure.Id) ?? default;

            string reason = failure.Win32Error switch
            {
                0    => "key_not_on_layout",
                1409 => "chord_taken",
                _    => "register_failed",
            };
            DeckleAppSource.Log.HotkeyRegistrationFailed(
                action?.ToggleId ?? "unknown", HotkeyChordLabel(failure.Id), reason, failure.Win32Error);
            DeckleAppSource.Log.HotkeyRegistrationFailedDetail(
                action?.ToggleId ?? "unknown", failure.Id, binding.Modifiers, binding.ScanCode, failure.Win32Error);

            // Error 0: the scancode has no key under the active layout, so
            // nobody owns it; every other error is a chord taken elsewhere.
            string body = failure.Win32Error == 0
                ? $"{label}: this key is not on the current keyboard layout."
                : $"{label}: another app owns this shortcut.";
            DeckleAppSource.Log.UserFeedbackEmitted(
                2, // Error
                "Hotkey unavailable",
                body,
                1); // Overlay
        }
    }

    // SettingsService.Changed fires after the debounced disk write, on the
    // store's timer thread (JsonSettingsStore), or on the caller's thread for
    // a Flush/Reload. RegisterHotKey binds to the message-only window's
    // thread, the UI thread, so the rebind is marshalled there.
    private void OnShellSettingsChangedForHotkeys()
    {
        _hudWindow?.DispatcherQueue.TryEnqueueOrLog(
            RebindHotkeysIfChanged, "HOTKEY", "hotkey rebind");
    }

    private void RebindHotkeysIfChanged()
    {
        if (_hotkeyManager is null || !_hotkeysInstalled) return;

        IReadOnlyList<GlobalHotkeyBinding> bindings = BuildHotkeyBindings();
        if (bindings.SequenceEqual(_hotkeyManager.Bindings)) return;

        ReportHotkeyFailures(_hotkeyManager.Rebind(bindings));
    }
}
