using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Input.Trackpad;
using Deckle.Shell;

namespace Deckle.Input.Trackpad;

// ── TrackpadPage ────────────────────────────────────────────────────────────
//
// Settings page for the Trackpad module — macOS-style three-finger drag on
// the Magic Trackpad 2. Resolved by the Settings NavigationView via the item
// Tag "Deckle.Input.Trackpad.TrackpadPage, Deckle.Input.Trackpad".
//
// Two kinds of surface, two persistence styles :
//
//   • Persisted settings (master switch, drag speed, raw-frame recording)
//     bind through TrackpadViewModel — auto-save on every change, no
//     OK/Cancel.
//
//   • Windows-integration acts (neutralize / restore gestures, repair the
//     Bluetooth pairing bug, toggle elevated startup) are imperative commands
//     driven from here. They can fail or be UAC-cancelled, so the page NEVER
//     assumes success : each handler guards against exceptions, disables its
//     control for the duration of the call, then re-reads the real state back
//     into the UI. The two-state gesture button and the elevated toggle are
//     refreshed from their service's actual state, not from the click.
//
// Same patterns as DiagnosticsPage / AmbientPage : NavigationCacheMode
// Required, an _initializing guard around the initial control sync.
public sealed partial class TrackpadPage : Page
{
    public TrackpadViewModel ViewModel { get; } = new();

    // Suppresses the elevated toggle's Toggled handler while we revert the
    // switch to the real state after a failed / cancelled Enable/Disable —
    // the revert would otherwise re-enter the handler.
    private bool _suppressElevatedToggle;

    // Drive the two composed section hosts. Held in fields so their subscriptions
    // to the ViewModel live as long as the (cached) page — the same host-only
    // pattern GeneralPage/DiagnosticsPage use.
    private SettingsComposer? _dragComposer;
    private SettingsComposer? _diagnosticsComposer;

    public TrackpadPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeSettings();
        SyncImperativeControls();
    }

    // ── Composed persisted settings ──────────────────────────────────────────
    //
    // Host-only, like GeneralPage's sections: the page hands each section's host
    // panel and its manifest (declared in TrackpadViewModel.Settings.cs) to a
    // composer — the drag section (master toggle + drag-speed slider) and the
    // diagnostics section (raw-frame recording), one composer each so the two
    // section headers keep their on-screen order. The composers subscribe to the
    // ViewModel, so the controls reflect Load() (and each card's inline reset)
    // with no code-behind sync — the change handlers still live in the VM's
    // partial setters (Push + Save), which the composers drive through the
    // descriptor setters.
    private void ComposeSettings()
    {
        _dragComposer = new SettingsComposer(DragHost, ViewModel);
        _dragComposer.Compose(ViewModel.TrackpadDragSettingsManifest);

        _diagnosticsComposer = new SettingsComposer(DiagnosticsHost, ViewModel);
        _diagnosticsComposer.Compose(ViewModel.TrackpadDiagnosticsSettingsManifest);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Re-read the act-backed controls — a neutralize / elevated change
        // could have happened elsewhere while the page was cached.
        ViewModel.Load();
        SyncImperativeControls();
    }

    // Refreshes the two controls whose state is owned by a service rather
    // than by settings : the gesture button's label and the elevated toggle.
    private void SyncImperativeControls()
    {
        RefreshGesturesLabel();

        _suppressElevatedToggle = true;
        try { ElevatedToggle.IsOn = SafeIsElevatedEnabled(); }
        finally { _suppressElevatedToggle = false; }
    }

    // ── Windows three-finger gestures : turn off / restore ──────────────────

    // Three states, because "restore" is only honest when Deckle holds the
    // backup. Gestures active → offer to turn them off. Gestures off with a
    // Deckle backup → offer to restore that backup. Gestures off with no
    // backup (the user turned them off themselves, in Windows Settings) →
    // nothing Deckle can restore ; the button reports the state, disabled.
    private void RefreshGesturesLabel()
    {
        bool neutralized = SafeAreNeutralized();
        bool hasBackup   = SafeHasBackup();

        if (!neutralized)
        {
            GesturesButtonLabel.Text = Loc.Get("TrackpadPage_GesturesButton_Neutralize");
            GesturesButton.IsEnabled = true;
        }
        else if (hasBackup)
        {
            GesturesButtonLabel.Text = Loc.Get("TrackpadPage_GesturesButton_Restore");
            GesturesButton.IsEnabled = true;
        }
        else
        {
            GesturesButtonLabel.Text = Loc.Get("TrackpadPage_GesturesButton_AlreadyOff");
            GesturesButton.IsEnabled = false;
        }
    }

    private void GesturesButton_Click(object sender, RoutedEventArgs e)
    {
        GesturesButton.IsEnabled = false;
        try
        {
            if (!SafeAreNeutralized())
            {
                WindowsGestureNeutralizer.TryNeutralize();
            }
            else if (SafeHasBackup())
            {
                WindowsGestureNeutralizer.TryRestore();
            }
        }
        catch
        {
            // An act failure must never crash the page — the label refresh
            // below reflects whatever state actually took effect.
        }
        finally
        {
            // Re-reads the real state and re-enables (or not) accordingly.
            RefreshGesturesLabel();
        }
    }

    // ── Repair Bluetooth connection ─────────────────────────────────────────

    private void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        try
        {
            // Launches the elevated repair script (UAC prompt + interactive
            // console). Nothing to read back — the work happens in the
            // separate console the user drives.
            ConnectionRepair.TryLaunch();
        }
        catch
        {
            // Swallow : a launch failure (UAC declined, script missing)
            // leaves the page untouched.
        }
        finally
        {
            RepairButton.IsEnabled = true;
        }
    }

    // ── Start elevated ──────────────────────────────────────────────────────

    private void ElevatedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressElevatedToggle) return;

        ElevatedToggle.IsEnabled = false;
        try
        {
            // Enable/Disable can fail or be UAC-cancelled — never assume the
            // requested state took. Apply the request, then read the actual
            // state back into the switch.
            if (ElevatedToggle.IsOn)
            {
                ElevatedStartupService.Enable();
            }
            else
            {
                ElevatedStartupService.Disable();
            }
        }
        catch
        {
            // Fall through to the re-read so the switch reflects reality.
        }
        finally
        {
            _suppressElevatedToggle = true;
            try { ElevatedToggle.IsOn = SafeIsElevatedEnabled(); }
            finally { _suppressElevatedToggle = false; }
            ElevatedToggle.IsEnabled = true;
        }
    }

    // ── Defensive reads ─────────────────────────────────────────────────────
    // Each act's state query can throw (registry / scheduled-task access) ;
    // a query failure resolves to the safe "off" reading rather than
    // bringing the page down.

    private static bool SafeAreNeutralized()
    {
        try { return WindowsGestureNeutralizer.AreNeutralized(); }
        catch { return false; }
    }

    private static bool SafeHasBackup()
    {
        try { return WindowsGestureNeutralizer.HasBackup(); }
        catch { return false; }
    }

    private static bool SafeIsElevatedEnabled()
    {
        try { return ElevatedStartupService.IsEnabled(); }
        catch { return false; }
    }
}
