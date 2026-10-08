using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Controls;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Lighting.Ambient;

// Per-light settings of the "Colour per zone" mode: for each light of the
// selected Hue group, the screen edge it follows (AmbientSettings.LightZones),
// its brightness (AmbientSettings.LightBrightness, 1.0 when absent) and an
// Identify button that flashes it for three seconds.
//
// Visible only while UseMultiLight is on, a bridge is paired and a group is
// selected (HueLastGroupId) — hidden, never greyed, otherwise. The group's
// lights come from LightPlacementResolver; a light with no stored zone takes
// the zone its entertainment-area position suggests, written to the store
// once so the engine and this card agree.
//
// Sync: AmbientSettingsService.Changed and HuePairingService.BridgeChanged
// fire off the UI thread and are marshalled here. A new group (or bridge)
// resolves and rebuilds the rows; any other change refreshes the existing
// rows in place, so a slider being dragged is never torn down by the save
// it just triggered.
public sealed partial class LightZonesCard : UserControl
{
    // DropDownButton + MenuFlyout rather than ComboBox: in the Playground, a
    // ComboBox built in code-behind missed its first click (popup measure /
    // focus quirk); the flyout pair is deterministic.
    private static readonly (LightZone Zone, string LabelKey)[] ZoneOptions =
    [
        (LightZone.None,   "AmbientLights_Zone_None"),
        (LightZone.Top,    "AmbientLights_Zone_Top"),
        (LightZone.Bottom, "AmbientLights_Zone_Bottom"),
        (LightZone.Left,   "AmbientLights_Zone_Left"),
        (LightZone.Right,  "AmbientLights_Zone_Right"),
    ];

    // The bridge's own identify flash (lselect) lasts 15 s; three are enough
    // to spot the lamp, then StopIdentifyAsync cuts it short.
    private static readonly TimeSpan IdentifyFlashDuration = TimeSpan.FromSeconds(3);

    private sealed record LightRow(string LightId, DropDownButton ZoneButton, Slider BrightnessSlider);

    private readonly List<LightRow> _rows = new();
    private LightPlacement? _placement;
    private string? _resolvedGroupId;
    private CancellationTokenSource? _resolveCts;
    private bool _syncingRows;

    public LightZonesCard()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AmbientSettingsService.Instance.Changed  += OnSettingsChanged;
        HuePairingService.Instance.BridgeChanged += OnBridgeChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AmbientSettingsService.Instance.Changed  -= OnSettingsChanged;
        HuePairingService.Instance.BridgeChanged -= OnBridgeChanged;
        CancelResolve();
        // Resolve again on the next Loaded: the group may have changed
        // its lights while the page was away.
        _resolvedGroupId = null;
    }

    private void OnSettingsChanged()
        => DispatcherQueue.TryEnqueueObserved(
            operation: "settings-reload", caller: "ambient-light-zones-card",
            callback: Refresh,
            rejectSource: "AMBIENT", rejectWhat: "light zones refresh");

    private void OnBridgeChanged()
        => DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "ambient-light-zones-card",
            callback: () =>
            {
                _resolvedGroupId = null;
                Refresh();
            },
            rejectSource: "AMBIENT", rejectWhat: "light zones bridge sync");

    // UI thread. Decides visibility, then either resolves a new group or
    // refreshes the rows already built.
    private void Refresh()
    {
        var settings = AmbientSettingsService.Instance.Current;
        string? groupId = settings.HueLastGroupId;
        var bridge = HuePairingService.Instance.Bridge;

        bool show = settings.UseMultiLight
                 && !string.IsNullOrEmpty(groupId)
                 && bridge is { IsPaired: true };
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        if (!string.Equals(groupId, _resolvedGroupId, StringComparison.Ordinal))
        {
            _ = ResolveAsync(bridge!, groupId!);
            return;
        }

        SyncRows();
    }

    private async Task ResolveAsync(HueBridgeClient bridge, string groupId)
    {
        CancelResolve();
        var cts = new CancellationTokenSource();
        _resolveCts = cts;
        _resolvedGroupId = groupId;

        ClearRows();
        ResolvingRing.IsActive = true;
        try
        {
            var placement = await LightPlacementResolver
                .ResolveAsync(bridge, groupId, cts.Token)
                .ConfigureAwait(true);
            if (cts.IsCancellationRequested) return;

            _placement = placement;
            BuildRows();
        }
        catch (OperationCanceledException)
        {
            // A newer group or an Unloaded took over.
        }
        finally
        {
            if (ReferenceEquals(_resolveCts, cts)) ResolvingRing.IsActive = false;
        }
    }

    private void CancelResolve()
    {
        try { _resolveCts?.Cancel(); } catch { /* best effort */ }
        _resolveCts?.Dispose();
        _resolveCts = null;
    }

    private void ClearRows()
    {
        _placement = null;
        _rows.Clear();
        LightsExpander.Items.Clear();
        EmptyStateText.Visibility = Visibility.Collapsed;
    }

    private void BuildRows()
    {
        _rows.Clear();
        LightsExpander.Items.Clear();

        var lights = _placement?.Lights ?? [];
        EmptyStateText.Visibility = lights.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (lights.Count == 0) return;

        var settings = AmbientSettingsService.Instance.Current;
        bool suggestionWritten = false;

        _syncingRows = true;
        try
        {
            foreach (var light in lights)
            {
                // A stored zone wins; otherwise the entertainment-area
                // suggestion is written once so the engine drives the light.
                if (!settings.LightZones.TryGetValue(light.Id, out var zone) || zone == LightZone.None)
                {
                    zone = LightZone.None;
                    if (_placement!.SuggestedZones.TryGetValue(light.Id, out var suggested)
                        && suggested != LightZone.None)
                    {
                        zone = suggested;
                        WriteZone(settings, light.Id, suggested);
                        suggestionWritten = true;
                    }
                }

                LightsExpander.Items.Add(BuildRow(light, zone, settings));
            }
        }
        finally
        {
            _syncingRows = false;
        }

        if (suggestionWritten) AmbientSettingsService.Instance.Save();
    }

    private SettingsCard BuildRow(LightDescriptor light, LightZone zone, AmbientSettings settings)
    {
        var zoneButton = new DropDownButton
        {
            Content  = LabelFor(zone),
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(zoneButton, Loc.Get("AmbientLights_ZoneName"));
        var flyout = new MenuFlyout();
        foreach (var option in ZoneOptions)
        {
            var item = new MenuFlyoutItem { Text = Loc.Get(option.LabelKey) };
            var target = option.Zone;
            item.Click += (_, _) => OnZonePicked(light.Id, target);
            flyout.Items.Add(item);
        }
        zoneButton.Flyout = flyout;

        var slider = new Slider
        {
            Minimum       = 0,
            Maximum       = 100,
            StepFrequency = 1,
            SmallChange   = 1,
            LargeChange   = 10,
            Width         = 160,
            VerticalAlignment = VerticalAlignment.Center,
            Value         = BrightnessPercentOf(settings, light.Id),
        };
        AutomationProperties.SetName(slider, Loc.Get("AmbientLights_BrightnessName"));
        ToolTipService.SetToolTip(slider, Loc.Get("AmbientLights_BrightnessName"));
        slider.ValueChanged += (_, args) => OnBrightnessChanged(light.Id, args.NewValue);

        var identifyButton = new Button
        {
            Content   = Loc.Get("AmbientLights_Identify"),
            IsEnabled = light.IsReachable,
        };
        identifyButton.Click += (_, _) => _ = IdentifyAsync(identifyButton, light.Id);

        _rows.Add(new LightRow(light.Id, zoneButton, slider));

        var card = new SettingsCard
        {
            Header      = light.Name,
            Content     = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 8,
                Children    = { slider, zoneButton, identifyButton },
            },
        };
        if (!light.IsReachable) card.Description = Loc.Get("AmbientLights_Offline");
        return card;
    }

    // Reflects the store on the rows already built (an edit from another
    // surface, a preset, a reset). Guarded so the slider does not write back.
    private void SyncRows()
    {
        var settings = AmbientSettingsService.Instance.Current;
        _syncingRows = true;
        try
        {
            foreach (var row in _rows)
            {
                var zone = settings.LightZones.TryGetValue(row.LightId, out var z) ? z : LightZone.None;
                row.ZoneButton.Content = LabelFor(zone);

                double percent = BrightnessPercentOf(settings, row.LightId);
                if (Math.Abs(row.BrightnessSlider.Value - percent) > 0.5)
                    row.BrightnessSlider.Value = percent;
            }
        }
        finally
        {
            _syncingRows = false;
        }
    }

    // Writes replace the whole dictionary instead of mutating it: the push
    // loop reads these maps on its own thread every tick, and swapping a
    // reference is atomic where adding a key to a live Dictionary is not.
    private static void WriteZone(AmbientSettings settings, string lightId, LightZone zone)
    {
        var zones = new Dictionary<string, LightZone>(settings.LightZones);
        if (zone == LightZone.None) zones.Remove(lightId);
        else                        zones[lightId] = zone;
        settings.LightZones = zones;
    }

    private void OnZonePicked(string lightId, LightZone zone)
    {
        WriteZone(AmbientSettingsService.Instance.Current, lightId, zone);
        AmbientSettingsService.Instance.Save();

        DeckleAmbientSource.Log.LightZoneUpdated();
        DeckleAmbientSource.Log.LightZoneUpdatedDetail(lightId, zone.ToString());

        // Relabel at once; the debounced Changed refresh lands later.
        foreach (var row in _rows)
            if (row.LightId == lightId) row.ZoneButton.Content = LabelFor(zone);
    }

    private void OnBrightnessChanged(string lightId, double percent)
    {
        if (_syncingRows) return;

        double value = Math.Clamp(percent / 100.0, 0.0, 1.0);
        var settings = AmbientSettingsService.Instance.Current;
        settings.LightBrightness = new Dictionary<string, double>(settings.LightBrightness)
        {
            [lightId] = value,
        };
        // Save is debounced by the store; the engine reads the live value on
        // its next tick, so the drag is felt at the lamp as it happens.
        AmbientSettingsService.Instance.Save();
        DeckleAmbientSource.Log.LightBrightnessChanged(lightId, value);
    }

    private async Task IdentifyAsync(Button button, string lightId)
    {
        if (_placement?.Output is not { } output) return;

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content   = Loc.Get("AmbientLights_Identifying");
        try
        {
            await output.IdentifyLightAsync(lightId).ConfigureAwait(true);
            await Task.Delay(IdentifyFlashDuration).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LightPlacementResolver.LogCallFailed("identify", ex);
        }
        finally
        {
            try { await output.StopIdentifyAsync(lightId).ConfigureAwait(true); }
            catch { /* best effort: the bridge ends the flash on its own */ }

            button.Content   = originalContent;
            button.IsEnabled = true;
        }
    }

    private static double BrightnessPercentOf(AmbientSettings settings, string lightId)
        => settings.LightBrightness.TryGetValue(lightId, out var b)
            ? Math.Clamp(b, 0.0, 1.0) * 100.0
            : 100.0;

    private static string LabelFor(LightZone zone)
    {
        foreach (var option in ZoneOptions)
            if (option.Zone == zone) return Loc.Get(option.LabelKey);
        return Loc.Get(ZoneOptions[0].LabelKey);
    }
}
