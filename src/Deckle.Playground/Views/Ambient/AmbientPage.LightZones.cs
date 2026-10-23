using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground;

// ─── AmbientPage — light zones ──────────────────────────────────────────────
//
// Resolves the lights of the selected Hue group, pre-fills suggestions from
// the matching entertainment area, and writes the user's picks into
// AmbientSettings.LightZones, which the engine reads on its next push tick.

public sealed partial class AmbientPage
{
    // DropDownButton + MenuFlyout, not ComboBox: a ComboBox built in
    // code-behind (Items.Add or ItemsSource) misses its first click, a WinUI 3
    // popup measure/focus quirk.
    private sealed record ZoneOption(LightZone Zone, string Label);

    private static readonly ZoneOption[] _zoneOptions =
    [
        new ZoneOption(LightZone.None,   "None"),
        new ZoneOption(LightZone.Top,    "Top"),
        new ZoneOption(LightZone.Bottom, "Bottom"),
        new ZoneOption(LightZone.Left,   "Left"),
        new ZoneOption(LightZone.Right,  "Right"),
    ];

    // Sits on MenuFlyoutItem.Tag so one handler serves every row without
    // per-row closures.
    private sealed record ZoneMenuTag(string LightId, string LightName, LightZone Zone, DropDownButton Button);

    // The bridge's alert=lselect flashes for 15 s; StopIdentifyAsync cuts it
    // after this.
    private static readonly TimeSpan IdentifyFlashDuration = TimeSpan.FromSeconds(3);

    private async Task ResolveLightsAndBuildPlacementAsync(HueGroup group)
    {
        if (_hueLightOutput is not IMultiLightOutput multi)
        {
            _placementLights = null;
            _suggestedZones  = null;
            ClearLightZonesUi();
            return;
        }

        List<LightDescriptor> lights;
        try
        {
            var resolved = await multi.ListLightsAsync().ConfigureAwait(true);
            lights = new List<LightDescriptor>(resolved);
        }
        catch (Exception ex)
        {
            DecklePlaygroundSource.Log.HueCallFailed("list lights", ex.GetType().Name);
            DecklePlaygroundSource.Log.HueCallFailedDetail("list lights", $"{ex.GetType().Name}: {ex.Message}");
            _placementLights = null;
            BuildLightZonesUi();
            return;
        }

        DecklePlaygroundSource.Log.ResolveLights(group.Id, group.Name, lights.Count);

        var matchedArea = await FindMatchingEntertainmentAreaAsync(group, lights).ConfigureAwait(true);

        if (lights.Count == 0 && matchedArea is { LightPlacements.Count: > 0 })
        {
            DecklePlaygroundSource.Log.EntertainmentAreaUsed();
            DecklePlaygroundSource.Log.EntertainmentAreaUsedDetail(matchedArea.Name, matchedArea.LightPlacements.Count);
            foreach (var p in matchedArea.LightPlacements)
            {
                lights.Add(new LightDescriptor(p.LightId, p.Name, IsReachable: true));
            }
        }

        _suggestedZones = matchedArea is not null
            ? BuildSuggestionsFromArea(matchedArea, lights)
            : null;

        _placementLights = lights;
        BuildLightZonesUi();
    }

    private async Task<HueEntertainmentArea?> FindMatchingEntertainmentAreaAsync(
        HueGroup group, IReadOnlyList<LightDescriptor> lights)
    {
        if (!HuePairingService.Instance.IsPaired) return null;

        IReadOnlyList<HueEntertainmentArea> areas;
        try
        {
            areas = await HuePairingService.Instance
                .ListEntertainmentConfigurationsAsync()
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DecklePlaygroundSource.Log.HueCallFailedDetail("list ent configs", $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (areas.Count == 0)
        {
            DecklePlaygroundSource.Log.MatchEntertainmentArea("no_areas", "", "", 0);
            return null;
        }

        foreach (var a in areas)
        {
            if (string.Equals(a.Name, group.Name, StringComparison.OrdinalIgnoreCase))
            {
                DecklePlaygroundSource.Log.MatchEntertainmentArea("name", a.Id, a.Name, 0);
                return a;
            }
        }

        if (lights.Count > 0)
        {
            var idSet = new HashSet<string>(lights.Count);
            foreach (var l in lights) idSet.Add(l.Id);
            HueEntertainmentArea? best = null;
            int bestOverlap = 0;
            foreach (var a in areas)
            {
                int overlap = 0;
                foreach (var p in a.LightPlacements)
                    if (idSet.Contains(p.LightId)) overlap++;
                if (overlap > bestOverlap) { best = a; bestOverlap = overlap; }
            }
            if (best is not null)
            {
                DecklePlaygroundSource.Log.MatchEntertainmentArea("overlap", best.Id, best.Name, bestOverlap);
                return best;
            }
        }

        DecklePlaygroundSource.Log.MatchEntertainmentArea("no_match", "", "", 0);
        return null;
    }

    private Dictionary<string, LightZone> BuildSuggestionsFromArea(
        HueEntertainmentArea area, IReadOnlyList<LightDescriptor> lights)
    {
        var lightIdSet = new HashSet<string>(lights.Count);
        foreach (var l in lights) lightIdSet.Add(l.Id);

        var suggestions = new Dictionary<string, LightZone>();
        foreach (var p in area.LightPlacements)
        {
            if (!lightIdSet.Contains(p.LightId)) continue;
            var zone = LightZoneSuggester.Suggest(p);
            suggestions[p.LightId] = zone;
            DecklePlaygroundSource.Log.ZoneSuggested(p.LightId, zone.ToString(), area.Name, p.X, p.Y, p.Z);
        }
        return suggestions;
    }

    private void BuildLightZonesUi()
    {
        ClearLightZonesUi();

        LightZonesCard.Visibility = Visibility.Visible;
        LayoutLightZonesOverlayFromViewbox();

        if (_placementLights is null || _placementLights.Count == 0)
        {
            LightZonesEmptyState.Visibility = Visibility.Visible;
            UpdateZoneOverlayHighlight();
            UpdatePreviewViewboxVisibility();
            return;
        }
        LightZonesEmptyState.Visibility = Visibility.Collapsed;

        var settings = AmbientSettingsService.Instance.Current;

        bool suggestionWritten = false;
        foreach (var light in _placementLights)
        {
            LightZone persistedZone = settings.LightZones.TryGetValue(light.Id, out var z)
                ? z
                : LightZone.None;

            LightZone suggestedZone = LightZone.None;
            if (_suggestedZones is not null && _suggestedZones.TryGetValue(light.Id, out var s))
                suggestedZone = s;

            LightZone effectiveZone;
            if (persistedZone != LightZone.None)
            {
                effectiveZone = persistedZone;
            }
            else if (suggestedZone != LightZone.None)
            {
                effectiveZone = suggestedZone;
                settings.LightZones[light.Id] = suggestedZone;
                suggestionWritten = true;
            }
            else
            {
                effectiveZone = LightZone.None;
            }

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 8,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var nameLabel = new TextBlock
            {
                Text   = light.IsReachable ? light.Name : $"{light.Name} (offline)",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var identifyButton = new Button
            {
                Tag = light.Id,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new FontIcon { Glyph = Glyphs.LightbulbFilled, FontSize = 14 },
                        new TextBlock { Text = "Identify" },
                    },
                },
                IsEnabled = light.IsReachable,
            };
            identifyButton.Click += OnIdentifyLightClick;

            var zoneButton = new DropDownButton
            {
                Content                    = LabelForZone(effectiveZone),
                MinWidth                   = 130,
                Tag                        = light.Id,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            var zoneFlyout = new MenuFlyout();
            foreach (var opt in _zoneOptions)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = opt.Label,
                    Tag  = new ZoneMenuTag(light.Id, light.Name, opt.Zone, zoneButton),
                };
                menuItem.Click += OnZoneMenuItemClick;
                zoneFlyout.Items.Add(menuItem);
            }
            zoneButton.Flyout = zoneFlyout;

            row.Children.Add(nameLabel);
            row.Children.Add(identifyButton);
            row.Children.Add(zoneButton);
            LightZonesPanel.Children.Add(row);
        }

        if (suggestionWritten) AmbientSettingsService.Instance.Save();

        LightZonesCard.Visibility = Visibility.Visible;
        UpdateZoneOverlayHighlight();
        UpdatePreviewViewboxVisibility();
    }

    private async void OnIdentifyLightClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not string lightId) return;
        if (_hueLightOutput is not IMultiLightOutput multi) return;

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content   = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new ProgressRing { Width = 14, Height = 14, IsActive = true },
                new TextBlock     { Text = "Flashing" },
            },
        };

        try
        {
            await multi.IdentifyLightAsync(lightId).ConfigureAwait(true);
            await Task.Delay(IdentifyFlashDuration).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DecklePlaygroundSource.Log.HueCallFailed("identify", ex.GetType().Name);
            DecklePlaygroundSource.Log.HueCallFailedDetail("identify", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { await multi.StopIdentifyAsync(lightId).ConfigureAwait(true); }
            catch { /* best effort */ }

            button.Content   = originalContent;
            button.IsEnabled = true;
        }
    }

    private void ClearLightZonesUi()
    {
        LightZonesPanel.Children.Clear();
        LightZonesCard.Visibility       = Visibility.Collapsed;
        LightZonesEmptyState.Visibility = Visibility.Collapsed;
        ZoneTopRect.Visibility    = Visibility.Collapsed;
        ZoneBottomRect.Visibility = Visibility.Collapsed;
        ZoneLeftRect.Visibility   = Visibility.Collapsed;
        ZoneRightRect.Visibility  = Visibility.Collapsed;
        UpdatePreviewViewboxVisibility();
    }

    private void LayoutLightZonesOverlayFromViewbox()
    {
        // Before the sampler reports, assume the typical 30×17 grid.
        int cols = _previewGridCols > 0 ? _previewGridCols : 30;
        int rows = _previewGridRows > 0 ? _previewGridRows : 17;
        double nativeW = cols * PreviewCellSize;
        double nativeH = rows * PreviewCellSize;

        // A Stretch=Uniform Viewbox around an empty Grid measures to 0 dip,
        // so on the zones-only path (lights resolved before any sample) its
        // ActualWidth stays 0 while Visible. Fall back to the parent's size;
        // once cells arrive the two converge.
        double availW = AmbientPreviewViewbox.ActualWidth;
        double availH = AmbientPreviewViewbox.ActualHeight;
        if ((availW <= 0 || availH <= 0)
            && AmbientPreviewViewbox.Parent is FrameworkElement viewboxHost)
        {
            availW = viewboxHost.ActualWidth;
            availH = viewboxHost.ActualHeight;
        }
        if (availW <= 0 || availH <= 0) return;

        // Replicates Viewbox Stretch=Uniform; the Canvas's Center alignment
        // matches its letterboxing.
        double scale = Math.Min(availW / nativeW, availH / nativeH);
        double displayedW = nativeW * scale;
        double displayedH = nativeH * scale;

        LightZonesOverlay.Width  = displayedW;
        LightZonesOverlay.Height = displayedH;
        LayoutLightZoneRects(displayedW, displayedH);
    }

    private void LayoutLightZoneRects(double stageWidth, double stageHeight)
    {
        // Both branches clamp to stay in lockstep with
        // AmbientZoneSampler.ResolveBandCells on a hand-edited settings.json.
        var settings = AmbientSettingsService.Instance.Current;
        int cols = _previewGridCols > 0 ? _previewGridCols : 30;
        int rows = _previewGridRows > 0 ? _previewGridRows : 17;
        double bandH, bandV;
        if (settings.BorderMode == BorderThicknessMode.Cells)
        {
            int cellsRows = Math.Clamp(settings.BorderCells, 1, rows);
            int cellsCols = Math.Clamp(settings.BorderCells, 1, cols);
            bandH = stageHeight * cellsRows / rows;
            bandV = stageWidth  * cellsCols / cols;
        }
        else
        {
            double depth = Math.Clamp(settings.BorderDepth, 0.05, 0.5);
            bandH = stageHeight * depth;
            bandV = stageWidth  * depth;
        }

        Canvas.SetLeft(ZoneTopRect, 0);
        Canvas.SetTop (ZoneTopRect, 0);
        ZoneTopRect.Width  = stageWidth;
        ZoneTopRect.Height = bandH;

        Canvas.SetLeft(ZoneBottomRect, 0);
        Canvas.SetTop (ZoneBottomRect, stageHeight - bandH);
        ZoneBottomRect.Width  = stageWidth;
        ZoneBottomRect.Height = bandH;

        Canvas.SetLeft(ZoneLeftRect, 0);
        Canvas.SetTop (ZoneLeftRect, 0);
        ZoneLeftRect.Width  = bandV;
        ZoneLeftRect.Height = stageHeight;

        Canvas.SetLeft(ZoneRightRect, stageWidth - bandV);
        Canvas.SetTop (ZoneRightRect, 0);
        ZoneRightRect.Width  = bandV;
        ZoneRightRect.Height = stageHeight;

        UpdateZoneOverlayHighlight();
    }

    private void UpdateZoneOverlayHighlight()
    {
        bool hasTop    = false;
        bool hasBottom = false;
        bool hasLeft   = false;
        bool hasRight  = false;
        var settings = AmbientSettingsService.Instance.Current;
        if (_placementLights is not null)
        {
            foreach (var light in _placementLights)
            {
                if (!settings.LightZones.TryGetValue(light.Id, out var z)) continue;
                switch (z)
                {
                    case LightZone.Top:    hasTop    = true; break;
                    case LightZone.Bottom: hasBottom = true; break;
                    case LightZone.Left:   hasLeft   = true; break;
                    case LightZone.Right:  hasRight  = true; break;
                }
            }
        }
        ZoneTopRect.Visibility    = hasTop    ? Visibility.Visible : Visibility.Collapsed;
        ZoneBottomRect.Visibility = hasBottom ? Visibility.Visible : Visibility.Collapsed;
        ZoneLeftRect.Visibility   = hasLeft   ? Visibility.Visible : Visibility.Collapsed;
        ZoneRightRect.Visibility  = hasRight  ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string LabelForZone(LightZone zone)
    {
        for (int i = 0; i < _zoneOptions.Length; i++)
            if (_zoneOptions[i].Zone == zone) return _zoneOptions[i].Label;
        return _zoneOptions[0].Label;
    }

    private void OnZoneMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        if (item.Tag is not ZoneMenuTag tag) return;

        tag.Button.Content = item.Text;

        var settings = AmbientSettingsService.Instance.Current;
        if (tag.Zone == LightZone.None)
        {
            settings.LightZones.Remove(tag.LightId);
        }
        else
        {
            settings.LightZones[tag.LightId] = tag.Zone;
        }
        AmbientSettingsService.Instance.Save();

        DecklePlaygroundSource.Log.LightZoneUpdated();
        DecklePlaygroundSource.Log.LightZoneUpdatedDetail(tag.LightId, tag.Zone.ToString());

        UpdateZoneOverlayHighlight();
    }
}
