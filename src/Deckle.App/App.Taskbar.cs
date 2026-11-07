using Deckle.Shell.Taskbar;

namespace Deckle.App;

public partial class App
{
    // Why a tray pass runs, as the promoter's log spells it.
    private const string TrayTriggerStartup = "startup";
    private const string TrayTriggerSetting = "setting";
    private const string TrayTriggerExplorerRestart = "explorer_restart";

    // The tray setting last acted on, null before the first pass. Settings
    // changes arrive on the store's timer thread, startup and an Explorer
    // restart on the UI thread, so the three go through one gate.
    private readonly object _taskbarGate = new();
    private TrayIconPromoter? _trayPromoter;
    private bool? _trayWanted;

    private void InitializeTaskbar()
    {
        _trayPromoter = TrayIconPromoter.Instance;

        TaskbarSettingsService.Instance.Changed += ReconcileTaskbarTraySetting;
        ReconcileTaskbarTray(TrayTriggerStartup);
    }

    private void ReconcileTaskbarTraySetting() => ReconcileTaskbarTray(TrayTriggerSetting);

    // The setting decides: on promotes every icon and watches for new ones, off
    // gives back what the promoter recorded. A change that leaves the setting as
    // it was (the store raises Changed for any write) costs nothing. At startup
    // an off still asks for a restore: it is a no-op unless a snapshot was left
    // behind, by a restore that failed or a settings file edited by hand.
    private void ReconcileTaskbarTray(string trigger)
    {
        if (_trayPromoter is not { } promoter) return;

        lock (_taskbarGate)
        {
            bool wanted = TaskbarSettingsService.Instance.Current.ShowAllTrayIcons;
            if (_trayWanted == wanted) return;
            _trayWanted = wanted;

            if (wanted) promoter.Apply(trigger);
            else promoter.Restore();
        }
    }

    // Explorer rebuilt the taskbar: the promoter's watcher is still armed on its
    // key, but explorer may have rewritten the icons' values meanwhile, so one
    // more pass puts them back. Runs inside the tray window's message handler,
    // so nothing may escape from here.
    private void ReapplyTaskbarTrayAfterExplorerRestart()
    {
        try
        {
            if (_trayPromoter is not { } promoter) return;

            lock (_taskbarGate)
            {
                if (_trayWanted == true) promoter.Apply(TrayTriggerExplorerRestart);
            }
        }
        catch (Exception ex)
        {
            DeckleShellTaskbarSource.Log.ExplorerRestartReapplyFailed(ex.GetType().Name, ex.Message);
        }
    }

    // Unhook before disposing: a debounced settings flush landing during
    // shutdown would otherwise run a pass behind Dispose. The flush then
    // persists a toggle still sitting in the debounce window. Dispose stops the
    // watcher and gives nothing back: the icons stay promoted until the setting
    // is turned off, and the snapshot waits in its file.
    private void ShutdownTaskbar()
    {
        TaskbarSettingsService.Instance.Changed -= ReconcileTaskbarTraySetting;
        TaskbarSettingsService.Instance.Flush();
        _trayPromoter?.Dispose();
    }
}
