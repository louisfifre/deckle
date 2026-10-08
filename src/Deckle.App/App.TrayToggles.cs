using Deckle.Autocorrect;
using Deckle.Catalog;
using Deckle.Input.PrecisionScroll;
using Deckle.Input.Trackpad;
using Deckle.Lighting.Ambient;
using Deckle.Llm.Rewrite;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;

namespace Deckle.App;

// Module switches of the tray quick-actions panel. Each switch flips the
// module's persisted Enabled flag and saves; the module's own settings
// observer does the actual start or stop, exactly as when the flag changes
// from its Settings page. Presence-gated: an absent module has no switch.
// Ids name the switch in logs and stay stable.
public partial class App
{
    private static IReadOnlyList<TrayToggle> BuildTrayToggles(StartupContext context)
    {
        var toggles = new List<TrayToggle>
        {
            // Precision scrolling and taskbar cover are always composed.
            new(
                "precision-scroll",
                Loc.Get("TrayMenu_PrecisionScroll"),
                () => PrecisionScrollSettingsService.Instance.Current.Enabled,
                () =>
                {
                    var s = PrecisionScrollSettingsService.Instance.Current;
                    s.Enabled = !s.Enabled;
                    PrecisionScrollSettingsService.Instance.Save();
                }),
        };

        if (context.TrackpadPresent)
        {
            // ReconcileTrackpad (App.Trackpad.cs) reacts to the save.
            toggles.Add(new(
                "trackpad",
                Loc.Get("TrayMenu_Trackpad"),
                () => TrackpadSettingsService.Instance.Current.Enabled,
                () =>
                {
                    var s = TrackpadSettingsService.Instance.Current;
                    s.Enabled = !s.Enabled;
                    TrackpadSettingsService.Instance.Save();
                }));
        }

        // The cover host start/stop reacts via the observer wired in
        // InitializeTaskbarCover.
        toggles.Add(new(
            "taskbar-cover",
            Loc.Get("TrayMenu_TaskbarCover"),
            () => TaskbarCoverSettingsService.Instance.Current.Enabled,
            () =>
            {
                var s = TaskbarCoverSettingsService.Instance.Current;
                s.Enabled = !s.Enabled;
                TaskbarCoverSettingsService.Instance.Save();
            }));

        if (context.AutocorrectPresent)
        {
            toggles.Add(new(
                "autocorrect",
                Loc.Get("TrayMenu_Autocorrect"),
                () => AutocorrectSettingsService.Instance.Current.Enabled,
                () =>
                {
                    bool enabled = AutocorrectSettingsService.Instance.Current.Enabled;
                    AutocorrectSettingsService.Instance.SetEnabled(!enabled);
                }));
        }

        if (context.RewritePresent)
        {
            // LlmSettings.Enabled is the master switch of the Rewriting page.
            toggles.Add(new(
                "rewrite",
                Loc.Get("TrayMenu_Rewrite"),
                () => LlmSettingsService.Instance.Current.Enabled,
                () =>
                {
                    var s = LlmSettingsService.Instance.Current;
                    s.Enabled = !s.Enabled;
                    LlmSettingsService.Instance.Save();
                }));
        }

        return toggles;
    }

    // Ambient Light last, after a divider: the most frequent switch, nearest
    // the icon since the panel opens upward from the taskbar. The engine
    // start/stop reacts through the AmbientSettingsService.Changed observer
    // wired in InitializeStartupShell.
    private static IReadOnlyList<TrayToggle> BuildTrayFooterToggles(StartupContext context)
    {
        if (!context.AmbientPresent)
            return [];

        return
        [
            new TrayToggle(
                "ambient",
                Loc.Get("TrayMenu_AmbientLight"),
                () => AmbientSettingsService.Instance.Current.Enabled,
                () =>
                {
                    var s = AmbientSettingsService.Instance.Current;
                    s.Enabled = !s.Enabled;
                    AmbientSettingsService.Instance.Save();
                }),
        ];
    }
}
