using Deckle.Shell.Taskbar;

namespace Deckle.App;

// The "Windows key shows the taskbar" setting in effect. WindowsKeyTaskbarFocus
// holds the shared input host only while the setting is on, so a person who
// leaves it off costs the keyboard nothing. A change applies live: the settings
// store raises Changed on its timer thread, and Reconcile is safe from there.
public partial class App
{
    private WindowsKeyTaskbarFocus? _windowsKeyFocus;

    private void InitializeTaskbarWindowsKey()
    {
        // The self-check composes none of the boot (see ComposedBySelfCheck); the
        // guard keeps it from ever sending keys on a machine it must not touch.
        if (IsSelfCheckMode(Environment.GetCommandLineArgs())) return;
        if (_keyboardMouseHost is null) return; // shared host not created, nothing to listen to

        _windowsKeyFocus = new WindowsKeyTaskbarFocus(_keyboardMouseHost, new WindowsKeyInjector());

        TaskbarSettingsService.Instance.Changed += ReconcileWindowsKeyFocus;
        ReconcileWindowsKeyFocus();
    }

    // The store raises Changed for any write; Reconcile costs nothing when the
    // setting is where it was.
    private void ReconcileWindowsKeyFocus() =>
        _windowsKeyFocus?.Reconcile(TaskbarSettingsService.Instance.Current.WindowsKeyShowsTaskbar);

    private void ShutdownTaskbarWindowsKey()
    {
        TaskbarSettingsService.Instance.Changed -= ReconcileWindowsKeyFocus;
        _windowsKeyFocus?.Dispose();
    }
}
