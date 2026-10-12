using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground;

// ─── AmbientPage — light zones (J4) ─────────────────────────────────────────
//
// Per-light zone-assignment UI : one row per fixture with Identify
// button + DropDownButton-driven zone picker. Resolves the lights from
// the selected Hue group, pre-fills suggestions from the matching
// entertainment area when available, and writes the user's picks into
// AmbientSettings.LightZones (persisted, consumed by the engine on the
// next push tick).

public sealed partial class AmbientPage
{
    // Zone options exposed by every per-light DropDownButton +
    // MenuFlyout pair. The set is static : five values, fixed shape,
    // never changes at runtime. We tried ComboBox first (both
    // Items.Add and ItemsSource) and both surfaced a "first click
    // doesn't open / select" bug that tracks back to WinUI 3 ComboBox
    // popup measure / focus quirks in code-behind construction.
    // DropDownButton + MenuFlyoutItem is deterministic.
    private sealed record ZoneOption(LightZone Zone, string Label);

    private static readonly ZoneOption[] _zoneOptions =
    [
        new ZoneOption(LightZone.None,   "None"),
        new ZoneOption(LightZone.Top,    "Top"),
        new ZoneOption(LightZone.Bottom, "Bottom"),
        new ZoneOption(LightZone.Left,   "Left"),
        new ZoneOption(LightZone.Right,  "Right"),
    ];

    // Carrier for everything OnZoneMenuItemClick needs : the lamp id
    // (to route the assignment), the lamp display name (for the Info
    // Capital log line), the zone the menu item represents, and the
    // DropDownButton that should be re-labelled on selection. Sits on
    // the MenuFlyoutItem.Tag so the handler is a plain delegate
    // without per-row closure capture.
    private sealed record ZoneMenuTag(string LightId, string LightName, LightZone Zone, DropDownButton Button);

    // Total UX duration of an Identify flash. The bridge would happily
    // run alert=lselect for 15 s ; we cap it at 3 s so the user spots
    // the lamp without sitting through a strobe, then we send alert=
    // none to cut the flash short.
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
            DecklePlaygroundSource.Log.HueCallFailed();
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
                        // Segoe Fluent E7E8 — lightbulb glyph, reads as
                        // "tell me which lamp this is". Spelled as a
                        // \uXXXX escape because the literal glyph char
                        // sits in a private-use range that some editors
                        // silently strip on save.
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
            DecklePlaygroundSource.Log.HueCallFailed();
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
        // Native footprint = grid dims × cell size. Falls back to the
        // typical 30×17 footprint while the sampler hasn't reported
        // anything yet — same default the zones-card path used.
        int cols = _previewGridCols > 0 ? _previewGridCols : 30;
        int rows = _previewGridRows > 0 ? _previewGridRows : 17;
        double nativeW = cols * PreviewCellSize;
        double nativeH = rows * PreviewCellSize;

        // Available area : the Viewbox's own ActualSize when it has
        // content to measure against, otherwise its parent (the inner
        // Grid hosting the preview surfaces). Stretch=Uniform on a
        // Viewbox with an empty Grid as content measures to 0 dip — it
        // can't stretch nothing — so on the zones-only path (page
        // reopened with the engine off, lights resolved before any
        // sample arrives) AmbientPreviewViewbox.ActualWidth stays at
        // zero even when the Viewbox is Visible. Falling back to the
        // parent's measured size lets the overlay anchor to the real
        // displayed area in that case ; once cells arrive the parent
        // and the Viewbox converge to the same value.
        double availW = AmbientPreviewViewbox.ActualWidth;
        double availH = AmbientPreviewViewbox.ActualHeight;
        if ((availW <= 0 || availH <= 0)
            && AmbientPreviewViewbox.Parent is FrameworkElement viewboxHost)
        {
            availW = viewboxHost.ActualWidth;
            availH = viewboxHost.ActualHeight;
        }
        if (availW <= 0 || availH <= 0) return;

        // Replicates Viewbox Stretch=Uniform : the limiting axis sets
        // the scale, the other axis gets letterboxed (centered by
        // default — which is what HorizontalAlignment/VerticalAlignment
        // Center on the Canvas matches).
        double scale = Math.Min(availW / nativeW, availH / nativeH);
        double displayedW = nativeW * scale;
        double displayedH = nativeH * scale;

        LightZonesOverlay.Width  = displayedW;
        LightZonesOverlay.Height = displayedH;
        LayoutLightZoneRects(displayedW, displayedH);
    }

    private void LayoutLightZoneRects(double stageWidth, double stageHeight)
    {
        // Pull the live thickness settings from the shared store so a
        // slider drag in the Playground repaints the overlay on the
        // next OnAmbientSettingsChanged tick. Share mode multiplies the
        // fraction by the matching stage dimension ; Cells mode
        // multiplies the cell count by the per-cell stage size derived
        // from the grid. Both branches clamp to stay in lockstep with
        // AmbientEngine.ResolveBandCells on a hand-edited settings.json.
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
