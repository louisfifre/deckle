using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Input.Trackpad;

namespace Deckle.Input.Trackpad;

// ── TrackpadPage ────────────────────────────────────────────────────────────
//
// Settings page for the Trackpad module. Resolved by the Settings NavigationView
// via the item Tag "Deckle.Input.Trackpad.TrackpadPage, Deckle.Input.Trackpad".
//
// The Windows-integration acts (neutralize / restore gestures, repair the
// Bluetooth pairing bug) can fail or be UAC-cancelled, so the page never
// assumes success: each handler guards against exceptions, disables its
// control for the duration of the call, then re-reads the real state back
// into the UI. The elevated logon task lives on Settings > General
// (GeneralViewModel.StartupSettings): it starts the whole app, not the drag.
public sealed partial class TrackpadPage : Page
{
    public TrackpadViewModel ViewModel { get; } = new();

    // Held in fields so their subscriptions to the ViewModel live as long as the
    // cached page.
    private SettingsComposer? _enabledComposer;
    private SettingsComposer? _dragComposer;

    public TrackpadPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ComposeSettings();
        RefreshGesturesLabel();
    }

    // ── Composed persisted settings ──────────────────────────────────────────
    private void ComposeSettings()
    {
        _enabledComposer = new SettingsComposer(EnabledHost, ViewModel);
        _enabledComposer.Compose(ViewModel.TrackpadEnabledSettingsManifest);

        _dragComposer = new SettingsComposer(DragHost, ViewModel);
        _dragComposer.Compose(ViewModel.TrackpadDragSettingsManifest);

        _enabledComposer.DirtyChanged += (_, _) => GateResetAll();
        _dragComposer.DirtyChanged += (_, _) => GateResetAll();
        GateResetAll();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Re-read the act-backed control — a neutralize could have happened
        // elsewhere while the page was cached.
        ViewModel.Load();
        RefreshGesturesLabel();
        // Load() raises no PropertyChanged on a clean profile, so no composer
        // DirtyChanged fires and this call settles the gate.
        GateResetAll();
    }

    // ── Whole-page "Reset all" ───────────────────────────────────────────────
    //
    // The Windows-integration acts are commands, not settings, so Reset all
    // leaves them alone.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled =
            (_enabledComposer?.IsDirty() ?? false) ||
            (_dragComposer?.IsDirty() ?? false);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _enabledComposer?.ResetAll();
        _dragComposer?.ResetAll();
    }

    // ── Windows three-finger gestures : turn off / restore ──────────────────

    // Three states, because "restore" is only honest when Deckle holds the
    // backup. Gestures active → offer to turn them off. Gestures off with a
    // Deckle backup → offer to restore that backup. Gestures off with no
    // backup (the user turned them off themselves, in Windows Settings) →
    // nothing Deckle can restore ; the button reports the state, disabled.
    // The control's state is owned by a service rather than by settings.
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
            RefreshGesturesLabel();
        }
    }

    // ── Repair Bluetooth connection ─────────────────────────────────────────

    private void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        try
        {
            // Nothing to read back: the work happens in the separate elevated
            // console the user drives.
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

    // ── Defensive reads ─────────────────────────────────────────────────────
    // Each act's state query can throw (registry access) ; a query failure
    // resolves to the safe "off" reading rather than bringing the page down.

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
}
