using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Shell;
using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// The tray side of the user incidents: a dot on the icon while one is open, and
// one line per module at the top of the left-click menu, "Problem · {module}",
// that opens the Settings page carrying the fix. The dot is a state, not an
// alert: it goes away when the incident closes, and nothing pops up.
public partial class App
{
    // The left-click menu without its incident lines, kept to recompose it.
    private IReadOnlyList<TrayMenuEntry> _trayActionsBase = [];

    // Set when the registry changed since the menu was last composed; read and
    // cleared on the UI thread only. The menu is recomposed at its next open
    // rather than at once, so an open menu never has its items swapped under
    // the pointer.
    private bool _trayIncidentLinesStale;

    // Called from InitializeStartupShell once the icon and the left-click menu
    // exist, before the icon is registered. An incident opened before this
    // point (the engine skipped at composition) is already in the registry and
    // shows from the icon's first mark.
    private void InitializeTrayIncidents(IReadOnlyList<TrayMenuEntry> trayActionsBase)
    {
        _trayActionsBase = trayActionsBase;
        TrayIncidentLines.DeclareAll(UserIncidentRegistry.Instance);
        UserIncidentRegistry.Instance.Changed += OnUserIncidentsChanged;
        ApplyUserIncidents();
    }

    // Raised on whatever thread opened or closed the incident. Every update
    // runs on the UI thread, where each one reads the registry when it runs:
    // the last one to run sees the latest state, so a burst from several
    // threads cannot leave the dot behind it.
    private void OnUserIncidentsChanged()
        => _hudWindow?.DispatcherQueue.TryEnqueueOrLog(
            ApplyUserIncidents, "TRAY", "user incident change");

    private void ApplyUserIncidents()
    {
        IReadOnlyList<TrayIncidentLines.Line> lines =
            TrayIncidentLines.Fold(UserIncidentRegistry.Instance.Snapshot());
        _tray?.SetIncidentIndicator(lines.Count > 0);
        _trayIncidentLinesStale = true;
    }

    private void ShowTrayActions()
    {
        if (_trayActions is null) return;
        if (_trayIncidentLinesStale)
        {
            _trayIncidentLinesStale = false;
            _trayActions.Entries = ComposeTrayActions();
        }
        _trayActions.Show();
    }

    private IReadOnlyList<TrayMenuEntry> ComposeTrayActions()
    {
        var lines = new List<TrayMenuEntry>();
        foreach (TrayIncidentLines.Line line in
                 TrayIncidentLines.Fold(UserIncidentRegistry.Instance.Snapshot()))
        {
            string module = line.Module;
            string pageTag = line.PageTag;
            lines.Add(new TrayMenuCommand(
                "incident-" + module,
                () => Loc.Format("TrayMenu_Incident_Format", TrayIncidentLines.LabelOf(module)),
                () => ShowSettingsWindowLazy(pageTag)));
        }
        return TrayIncidentLines.Compose(lines, _trayActionsBase);
    }
}
