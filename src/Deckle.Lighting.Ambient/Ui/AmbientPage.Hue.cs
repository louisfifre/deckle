using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientPage
{
    // Hue pairing local state. The countdown CTS is owned by this page
    // so the Unloaded handler can cancel an in-flight pair if the user
    // navigates away. _hueIsPairing guards double-clicks on the Pair
    // button. _hueGroupComboSuppress prevents the SelectionChanged
    // handler from firing while ListGroupsAsync is repopulating the
    // combo's Items collection.
    private CancellationTokenSource? _huePairCts;
    private bool _hueIsPairing;
    private HueBridge? _hueDiscoveredBridge;
    private IReadOnlyList<HueGroup> _hueGroups = [];
    private bool _hueGroupComboSuppress;

    // ── Hue pairing handlers ────────────────────────────────────────

    private void OnHueBridgeChanged()
    {
        // BridgeChanged can fire from any thread (Pair runs on a worker
        // task, Forget is direct from UI thread). Marshal to the UI
        // thread because the sync touches XAML elements.
        if (DispatcherQueue.HasThreadAccess) SyncHueBridgeUi();
        else                                 DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "ambient-page-hue",
            callback: SyncHueBridgeUi,
            rejectSource: "AMBIENT", rejectWhat: "hue bridge sync");
    }

    // Project HuePairingService state into the Hue expander visuals.
    // Idempotent : called on Loaded, on every BridgeChanged, and after
    // every local pair / forget operation. The pair status text (e.g.
    // "Waiting (30 s)") is owned by the individual handlers — this
    // method only touches the steady-state "Paired" / "Not paired"
    // label so it doesn't stomp transient UI mid-pair.
    private void SyncHueBridgeUi()
    {
        var paired = HuePairingService.Instance.PairedBridge;
        var bridge = HuePairingService.Instance.Bridge;

        if (paired is null || bridge is null || !bridge.IsPaired)
        {
            HueBridgeStatusDot.Fill   = GetThemeBrush("SystemFillColorNeutralBrush");
            HueBridgeStatusText.Text  = Loc.Get("AmbientHue_Status_NotPaired");
            HuePairLabel.Text         = Loc.Get("AmbientHue_PairLabel_Pair");
            HueListGroupsButton.IsEnabled = false;
            HueForgetButton.IsEnabled     = false;

            _hueGroupComboSuppress = true;
            HueGroupComboBox.Items.Clear();
            HueGroupComboBox.IsEnabled = false;
            _hueGroupComboSuppress = false;
            return;
        }

        HueBridgeStatusDot.Fill   = GetThemeBrush("SystemFillColorSuccessBrush");
        HueBridgeStatusText.Text  = Loc.Get("AmbientHue_Status_Paired");
        HueBridgeIpTextBox.Text   = paired.InternalIpAddress;
        HuePairLabel.Text         = Loc.Get("AmbientHue_PairLabel_Repair");
        HueListGroupsButton.IsEnabled = true;
        HueForgetButton.IsEnabled     = true;
    }

    private static Brush GetThemeBrush(string resourceKey)
        => (Brush)Application.Current.Resources[resourceKey];

    // Set the transient pair status caption — auto-collapse the
    // surrounding TextBlock when empty so the row disappears entirely
    // (vs leaving a hollow caption slot below the address card).
    private void SetHuePairStatus(string text)
    {
        HuePairStatusText.Text = text;
        HuePairStatusText.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void OnHueDiscoverClick(object sender, RoutedEventArgs e)
        => await DiscoverHueAsync(useCloud: false);

    private async void OnHueCloudDiscoverClick(object sender, RoutedEventArgs e)
        => await DiscoverHueAsync(useCloud: true);

    private async Task DiscoverHueAsync(bool useCloud)
    {
        Control activeButton = useCloud ? HueCloudDiscoverButton : HueDiscoverButton;
        activeButton.IsEnabled = false;
        if (!useCloud)
            HueCloudDiscoverButton.Visibility = Visibility.Collapsed;
        SetHuePairStatus(string.Empty);
        try
        {
            IReadOnlyList<HueBridge> bridges = useCloud
                ? await HuePairingService.Instance.DiscoverViaCloudAsync().ConfigureAwait(true)
                : await HuePairingService.Instance.DiscoverAsync().ConfigureAwait(true);
            if (bridges.Count > 0)
            {
                _hueDiscoveredBridge = bridges[0];
                HueBridgeIpTextBox.Text = _hueDiscoveredBridge.InternalIpAddress;
                SetHuePairStatus(Loc.Get(useCloud
                    ? "AmbientHue_Discovery_OnlineFound"
                    : "AmbientHue_Discovery_LocalFound"));
                if (useCloud)
                    HueCloudDiscoverButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                SetHuePairStatus(Loc.Get(useCloud
                    ? "AmbientHue_Discovery_OnlineEmpty"
                    : "AmbientHue_Discovery_LocalEmpty"));
                if (!useCloud)
                    HueCloudDiscoverButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (useCloud)
            {
                DeckleLightingSource.Log.DiscoveryFailed();
                DeckleLightingSource.Log.DiscoveryFailedDetail(ex.GetType().Name, ex.Message);
            }
            else
            {
                DeckleLightingSource.Log.LocalDiscoveryFailed();
                DeckleLightingSource.Log.LocalDiscoveryFailedDetail(ex.GetType().Name, ex.Message);
            }
            SetHuePairStatus(Loc.Get("AmbientHue_Discovery_Failed"));
        }
        finally
        {
            activeButton.IsEnabled = true;
        }
    }

    private async void OnHuePairClick(object sender, RoutedEventArgs e)
    {
        if (_hueIsPairing) return;

        var ip = HueBridgeIpTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            SetHuePairStatus(Loc.Get("AmbientHue_PairStatus_IpRequired"));
            return;
        }

        // Re-pairing over a LIVE pairing is destructive: PairAsync overwrites the
        // persisted ip/id/username, dropping the bridge we currently hold. So gate
        // it behind the shared destructive-confirm — but ONLY when a bridge is
        // already paired. The very first pairing has nothing to lose and stays
        // friction-free. Wording lives under AmbientHue_RepairDialog_* (mirrored in
        // Deckle.App); the service owns the Cancel verb.
        if (HuePairingService.Instance.IsPaired)
        {
            bool confirmed = await ConfirmationService.RequestAsync(
                this.XamlRoot,
                new ConfirmationRequest(
                    Loc.Get("AmbientHue_RepairDialog_Title"),
                    Loc.Get("AmbientHue_RepairDialog_Content"),
                    Loc.Get("Common_Replace"),
                    IsDestructive: true));
            if (!confirmed) return;
        }

        try { _huePairCts?.Cancel(); } catch { /* best effort */ }
        _huePairCts?.Dispose();
        _huePairCts = new CancellationTokenSource();
        _hueIsPairing = true;

        HuePairButton.IsEnabled = false;
        HuePairLabel.Text       = Loc.Get("AmbientHue_PairLabel_Waiting");
        SetHuePairStatus(Loc.Get("AmbientHue_PairStatus_PressLink"));

        var target = _hueDiscoveredBridge is { } discovered
                     && string.Equals(discovered.InternalIpAddress, ip, StringComparison.Ordinal)
            ? discovered
            : new HueBridge(Id: "manual", InternalIpAddress: ip, Port: 443);
        try
        {
            await HuePairingService.Instance
                .PairAsync(target, ct: _huePairCts.Token)
                .ConfigureAwait(true);
            SetHuePairStatus(Loc.Get("AmbientHue_PairStatus_Success"));
            // SyncHueBridgeUi fires via BridgeChanged event and flips
            // the dot to success + label to Re-pair.
        }
        catch (OperationCanceledException)
        {
            SetHuePairStatus(Loc.Get("AmbientHue_PairStatus_Cancelled"));
        }
        catch (TimeoutException)
        {
            SetHuePairStatus(Loc.Get("AmbientHue_PairStatus_TimedOut"));
        }
        catch (Exception ex)
        {
            SetHuePairStatus(Loc.Format("AmbientHue_PairStatus_Failed_Format", ex.Message));
            DeckleAmbientSource.Log.AmbientPagePairFailed();
            DeckleAmbientSource.Log.AmbientPagePairFailedDetail(ex.GetType().Name, ex.Message);
        }
        finally
        {
            _hueIsPairing = false;
            HuePairButton.IsEnabled = true;
        }
    }

    private async void OnHueListGroupsClick(object sender, RoutedEventArgs e)
    {
        if (!HuePairingService.Instance.IsPaired) return;

        HueListGroupsButton.IsEnabled = false;
        try
        {
            _hueGroups = await HuePairingService.Instance
                .ListGroupsAsync()
                .ConfigureAwait(true);

            _hueGroupComboSuppress = true;
            HueGroupComboBox.Items.Clear();
            foreach (var g in _hueGroups)
            {
                HueGroupComboBox.Items.Add(new ComboBoxItem
                {
                    Content = g.DisplayLabel,
                    Tag     = g,
                });
            }
            _hueGroupComboSuppress = false;

            HueGroupComboBox.IsEnabled = _hueGroups.Count > 0;
            if (_hueGroups.Count > 0)
            {
                string? lastId = AmbientSettingsService.Instance.Current.HueLastGroupId;
                int preselect = 0;
                if (!string.IsNullOrEmpty(lastId))
                {
                    for (int i = 0; i < _hueGroups.Count; i++)
                    {
                        if (_hueGroups[i].Id == lastId) { preselect = i; break; }
                    }
                }
                HueGroupComboBox.SelectedIndex = preselect;
            }
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.AmbientPageListGroupsFailed();
            DeckleAmbientSource.Log.AmbientPageListGroupsFailedDetail(ex.GetType().Name, ex.Message);
        }
        finally
        {
            HueListGroupsButton.IsEnabled = true;
        }
    }

    private void OnHueGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hueGroupComboSuppress) return;
        if (HueGroupComboBox.SelectedItem is not ComboBoxItem { Tag: HueGroup group }) return;

        // Persist the chosen group so AmbientEngine.StartAsync finds it
        // on the next pipeline start. Symmetric with the Playground
        // OnHueGroupSelectionChanged handler.
        AmbientSettingsService.Instance.Current.HueLastGroupId = group.Id;
        AmbientSettingsService.Instance.Save();
    }

    private async void OnHueForgetClick(object sender, RoutedEventArgs e)
    {
        // Clearing the pairing goes through the shared destructive-confirm gate
        // (Close is the default button; the verb must be reached on purpose).
        // Wording lives in Strings/en-US/Resources.resw under the
        // AmbientHue_ForgetDialog_* keys; the service owns the Cancel verb.
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("AmbientHue_ForgetDialog_Title"),
                Loc.Get("AmbientHue_ForgetDialog_Content"),
                Loc.Get("AmbientHue_ForgetDialog_PrimaryButton"),
                IsDestructive: true));
        if (!confirmed) return;

        HuePairingService.Instance.Forget();
        // SyncHueBridgeUi fires via BridgeChanged event ; we just clear
        // any transient pair status text so the row reads clean.
        SetHuePairStatus("");
        HueBridgeIpTextBox.Text = "";
    }
}
