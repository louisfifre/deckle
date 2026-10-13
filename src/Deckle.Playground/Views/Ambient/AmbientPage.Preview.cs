using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Deckle.Lighting;
using Deckle.Lighting.Ambient;
using Deckle.Shell;
using Deckle.Vision;

namespace Deckle.Playground;

// ─── AmbientPage — preview grid + swatches ──────────────────────────────────
//
// The timer prefers the canonical AmbientEngine.LatestSample and falls back
// to the local FrameSampler when only the page's own screen capture runs.
// With no live source the cells blank and the zone overlay turns neutral
// grey, so no captured frame stays frozen on screen.

public sealed partial class AmbientPage
{
    private void OnSamplerFrameArrived(CapturedFrame frame)
    {
        _frameSampler?.Process(frame);
    }

    private void BuildPreviewGrid(int cols, int rows)
    {
        AmbientPreviewGrid.RowDefinitions.Clear();
        AmbientPreviewGrid.ColumnDefinitions.Clear();
        AmbientPreviewGrid.Children.Clear();

        for (int c = 0; c < cols; c++)
            AmbientPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PreviewCellSize) });
        for (int r = 0; r < rows; r++)
            AmbientPreviewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PreviewCellSize) });

        _previewCells = new Microsoft.UI.Xaml.Shapes.Rectangle[cols * rows];

        // One SolidColorBrush per cell: the tick mutates each brush's Color,
        // so a shared brush would paint every cell with the grid's last pixel.
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                };
                Grid.SetRow(rect, r);
                Grid.SetColumn(rect, c);
                AmbientPreviewGrid.Children.Add(rect);
                _previewCells[r * cols + c] = rect;
            }
        }

        _previewGridCols = cols;
        _previewGridRows = rows;
        LayoutLightZonesOverlayFromViewbox();

        UpdatePreviewViewboxVisibility();
    }

    private void StartPreviewTimer()
    {
        if (_previewTimer is null)
        {
            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _previewTimer.Tick += OnPreviewTimerTick;
        }
        _previewTimer.Start();
    }

    // The timer runs only while a live source pushes samples: an idle 5 Hz
    // tick loads the dispatcher enough to lag window drags. Idempotent.
    private void EvaluatePreviewTimerState()
    {
        if (HasLiveSource())
        {
            StartPreviewTimer();
        }
        else
        {
            _previewTimer?.Stop();
            BlankIdlePreviewState();
        }
        UpdatePreviewViewboxVisibility();
    }

    private bool HasLiveSource()
    {
        var engine = AmbientEngine.Current;
        bool engineActive       = engine is { IsRunning: true };
        bool localCaptureActive = _screenCapture is { IsRunning: true };
        return engineActive || localCaptureActive;
    }

    private void BlankIdlePreviewState()
    {
        ClearPreviewCells();
        ApplyNeutralZoneOverlay();
        if (_swatchByLight.Count > 0)
        {
            EmittedSwatches.Children.Clear();
            _swatchByLight.Clear();
        }
    }

    private void UpdatePreviewViewboxVisibility()
    {
        // Visibility contract:
        //   live source                       → cells, no placeholder.
        //   no live source + Zones toggle on  → zone overlay, no placeholder.
        //   no live source + Zones toggle off → placeholder.
        //   no group selected (no zones)      → placeholder.
        //
        // Cells stay allocated for the cached page's lifetime, so they count
        // only while a live source lands fresh data.
        bool hasCells = _previewCells is not null && HasLiveSource();
        bool hasZones = _placementLights is { Count: > 0 };
        bool zonesToggleOn = ShowZoneOverlaysToggle?.IsChecked == true;
        bool show = hasCells || (hasZones && zonesToggleOn);

        AmbientPreviewViewbox.Visibility    = show ? Visibility.Visible : Visibility.Collapsed;
        AmbientPreviewEmptyState.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

        // Deferred past the next layout pass so the overlay reads the
        // Viewbox host's fresh size in the zones-only mode.
        if (show)
        {
            DispatcherQueue.TryEnqueueObserved(
                operation: "ui-update", caller: "playground-ambient-page-preview",
                callback: LayoutLightZonesOverlayFromViewbox,
                rejectSource: "PLAYGROUND", rejectWhat: "overlay layout",
                priority: DispatcherQueuePriority.Low);
        }
    }

    private void OnPreviewTimerTick(object? sender, object e)
    {
        var engine = AmbientEngine.Current;
        bool engineActive       = engine is { IsRunning: true };
        bool localCaptureActive = _screenCapture is { IsRunning: true };
        bool hasLiveSource      = engineActive || localCaptureActive;

        if (!hasLiveSource)
        {
            ClearPreviewCells();
            ApplyNeutralZoneOverlay();
            UpdateEmittedSwatches();   // self-clears when engine isn't running
            return;
        }

        var sample = engine?.LatestSample ?? _frameSampler?.LatestSample;

        if (sample is not null && _previewCells is null)
        {
            BuildPreviewGrid(sample.Cols, sample.Rows);
            UpdatePreviewViewboxVisibility();
        }

        if (sample is not null && _previewCells is not null)
        {
            int total = Math.Min(sample.Grid.Length, _previewCells.Length);
            for (int i = 0; i < total; i++)
            {
                var c = sample.Grid[i];
                var brush = _previewCells[i].Fill as SolidColorBrush;
                if (brush is null)
                {
                    _previewCells[i].Fill = new SolidColorBrush(c);
                }
                else
                {
                    brush.Color = c;
                }
            }
        }

        UpdateEmittedSwatches();
        UpdateZoneOverlayColors();
    }

    private void ClearPreviewCells()
    {
        if (_previewCells is null) return;
        foreach (var rect in _previewCells)
        {
            if (rect.Fill is SolidColorBrush b)
                b.Color = Microsoft.UI.Colors.Transparent;
        }
    }

    private void ApplyNeutralZoneOverlay()
    {
        ApplyNeutralZoneFill(LightZone.Top,    ZoneTopRect);
        ApplyNeutralZoneFill(LightZone.Bottom, ZoneBottomRect);
        ApplyNeutralZoneFill(LightZone.Left,   ZoneLeftRect);
        ApplyNeutralZoneFill(LightZone.Right,  ZoneRightRect);
    }

    private void ApplyNeutralZoneFill(LightZone zone, Microsoft.UI.Xaml.Shapes.Rectangle rect)
    {
        if (!_zoneFillBrushes.TryGetValue(zone, out var brush))
        {
            brush = new SolidColorBrush { Opacity = 0.4 };
            _zoneFillBrushes[zone] = brush;
            rect.Fill = brush;
        }
        // Mid-grey at 0.4 opacity reads as neutral on both Mica light and dark.
        brush.Color = Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
    }

    private void UpdateZoneOverlayColors()
    {
        var engine = AmbientEngine.Current;
        if (engine is null || !engine.IsRunning) return;

        var snapshot = engine.SnapshotEmittedColors();
        if (snapshot.Count == 0) return;

        var settings = AmbientSettingsService.Instance.Current;

        ApplyZoneOverlayColor(LightZone.Top,    ZoneTopRect,    snapshot, settings);
        ApplyZoneOverlayColor(LightZone.Bottom, ZoneBottomRect, snapshot, settings);
        ApplyZoneOverlayColor(LightZone.Left,   ZoneLeftRect,   snapshot, settings);
        ApplyZoneOverlayColor(LightZone.Right,  ZoneRightRect,  snapshot, settings);
    }

    private void ApplyZoneOverlayColor(
        LightZone zone,
        Microsoft.UI.Xaml.Shapes.Rectangle rect,
        IReadOnlyDictionary<string, LightColor> emitted,
        AmbientSettings settings)
    {
        var color = ResolveZoneEmittedColor(emitted, settings, zone);
        if (color is null) return;

        if (!_zoneFillBrushes.TryGetValue(zone, out var brush))
        {
            // Soft fill so the preview cells stay readable through it.
            brush = new SolidColorBrush
            {
                Opacity = 0.4,
            };
            _zoneFillBrushes[zone] = brush;
            rect.Fill = brush;
        }
        brush.Color = Windows.UI.Color.FromArgb(0xFF, color.Value.R, color.Value.G, color.Value.B);
    }

    private static LightColor? ResolveZoneEmittedColor(
        IReadOnlyDictionary<string, LightColor> emitted,
        AmbientSettings settings,
        LightZone zone)
    {
        if (!settings.UseMultiLight)
        {
            return emitted.TryGetValue("group", out var c) ? c : null;
        }
        foreach (var (id, assignedZone) in settings.LightZones)
        {
            if (assignedZone == zone && emitted.TryGetValue(id, out var c))
                return c;
        }
        return null;
    }

    private void UpdateEmittedSwatches()
    {
        var engine = AmbientEngine.Current;
        if (engine is null || !engine.IsRunning)
        {
            if (_swatchByLight.Count > 0)
            {
                EmittedSwatches.Children.Clear();
                _swatchByLight.Clear();
            }
            return;
        }

        var snapshot = engine.SnapshotEmittedColors();
        if (snapshot.Count == 0) return;

        bool setMatches = snapshot.Count == _swatchByLight.Count;
        if (setMatches)
        {
            foreach (var key in snapshot.Keys)
            {
                if (!_swatchByLight.ContainsKey(key)) { setMatches = false; break; }
            }
        }

        if (!setMatches)
        {
            EmittedSwatches.Children.Clear();
            _swatchByLight.Clear();
            foreach (var (id, color) in snapshot)
            {
                var displayName = ResolveLightDisplayName(engine, id);
                var swatch = BuildSwatch(displayName, color);
                _swatchByLight[id] = swatch;
                EmittedSwatches.Children.Add(swatch.Container);
            }
        }
        else
        {
            foreach (var (id, color) in snapshot)
            {
                var entry = _swatchByLight[id];
                entry.Fill.Color = Windows.UI.Color.FromArgb(0xFF, color.R, color.G, color.B);
            }
        }
    }

    private (Microsoft.UI.Xaml.Shapes.Rectangle Container, SolidColorBrush Fill, string DisplayName) BuildSwatch(string displayName, LightColor color)
    {
        var fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, color.R, color.G, color.B));
        var swatchRect = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 32,
            Height = 32,
            RadiusX = 4,
            RadiusY = 4,
            Fill = fill,
            Stroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            StrokeThickness = 1,
        };
        ToolTipService.SetToolTip(swatchRect, displayName);
        return (swatchRect, fill, displayName);
    }

    private static string ResolveLightDisplayName(AmbientEngine engine, string lightKey)
    {
        if (lightKey == "group") return "Group";

        var multi = engine.MultiLights;
        if (multi is not null)
        {
            foreach (var descriptor in multi)
            {
                if (descriptor.Id == lightKey)
                {
                    return string.IsNullOrWhiteSpace(descriptor.Name) ? lightKey : descriptor.Name;
                }
            }
        }
        return lightKey;
    }
}
