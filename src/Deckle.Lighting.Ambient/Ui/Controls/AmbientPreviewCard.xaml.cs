using System;
using System.Collections.Generic;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Deckle.Lighting.Ambient;

// Live preview of the running engine in the shared PreviewBlock: the sampled
// grid mirroring the screen as the stage, the colour each light receives
// (swatches) and the four zone bands tinted with their light's colour, with
// the capture's format, grid, measured cadence and output transport as
// readouts.
//
// A live instrument in Settings opens on a gesture, never on page open: the
// block starts collapsed (and is collapsed again on Unloaded, in case the page
// is cached), and the 200 ms refresh runs only while it is open AND the engine
// runs AND the user has not paused it. While the engine is off the whole card
// hides — there is nothing to show and nothing to configure here.
//
// Ratio: each cell is sourceWidth / cols by sourceHeight / rows in source
// pixels, and a uniform Viewbox scales the stage, so the preview has the
// display's exact shape. Without a source size yet, cells fall back to
// squares for one tick.
//
// Cadence: the engine counts the frames its capture delivers; two reads a
// second or more apart give the rate the capture really holds, which is what
// the readout shows — never the rate the user picked.
//
// Threading: AmbientEngine.StateChanged fires on the engine's thread and is
// marshalled to the UI thread; everything else runs on the UI thread.
public sealed partial class AmbientPreviewCard : UserControl
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    private const double FallbackCellSize = 16;

    // Soft fill so the cells stay readable through a zone band. Opacity on
    // the brush, not the rectangle, so a future stroke keeps full opacity.
    private const double ZoneFillOpacity = 0.4;

    private const double RateWindowSeconds = 1.0;

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly Dictionary<string, (Rectangle Swatch, SolidColorBrush Fill)> _swatches = new();
    private readonly Dictionary<LightZone, SolidColorBrush> _zoneBrushes = new();

    private AmbientEngine? _engine;
    private SolidColorBrush[]? _cellBrushes;
    private int _cols;
    private int _rows;
    private Windows.Graphics.SizeInt32? _builtSize;
    private double _stageWidth;
    private double _stageHeight;
    private long _rateFrames;
    private long _rateTimestamp;

    public AmbientPreviewCard()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
        Block.IsExpandedChanged += (_, _) => UpdateState();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _engine = AmbientEngine.Current;
        if (_engine is not null) _engine.StateChanged += OnEngineStateChanged;
        UpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_engine is not null) _engine.StateChanged -= OnEngineStateChanged;
        _engine = null;
        _timer.Stop();
        PauseToggle.IsChecked = false;
        Block.IsExpanded = false;
    }

    private void OnEngineStateChanged(AmbientEngineState state)
        => DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "ambient-preview-card",
            callback: UpdateState,
            rejectSource: "AMBIENT", rejectWhat: "preview state");

    private void ZonesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is { IsRunning: true } engine)
            UpdateZoneOverlay(engine, engine.SnapshotEmittedColors());
    }

    private void PauseToggle_Click(object sender, RoutedEventArgs e) => UpdateState();

    private void UpdateState()
    {
        bool running = _engine is { IsRunning: true };
        Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        bool paused = PauseToggle.IsChecked == true;
        if (running && Block.IsExpanded && !paused)
        {
            if (!_timer.IsEnabled)
            {
                _rateTimestamp = 0;
                _timer.Start();
                Refresh();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Refresh()
    {
        var engine = _engine;
        if (engine is not { IsRunning: true })
        {
            UpdateState();
            return;
        }

        var sample = engine.LatestSample;
        if (sample is null) return;

        var size = engine.SourceSize;
        if (_cellBrushes is null || sample.Cols != _cols || sample.Rows != _rows || size != _builtSize)
            BuildGrid(sample.Cols, sample.Rows, size);

        // One brush per cell (see BuildGrid): writing the colour in place.
        int total = Math.Min(sample.Grid.Length, _cellBrushes!.Length);
        for (int i = 0; i < total; i++)
            _cellBrushes[i].Color = sample.Grid[i];

        var emitted = engine.SnapshotEmittedColors();
        UpdateSwatches(engine, emitted);
        UpdateZoneOverlay(engine, emitted);
        UpdateReadouts(engine, size);
    }

    private void BuildGrid(int cols, int rows, Windows.Graphics.SizeInt32? size)
    {
        CellsGrid.RowDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Clear();
        CellsGrid.Children.Clear();

        double cellWidth  = size is { Width: > 0 } s1 ? (double)s1.Width / cols : FallbackCellSize;
        double cellHeight = size is { Height: > 0 } s2 ? (double)s2.Height / rows : FallbackCellSize;

        for (int c = 0; c < cols; c++)
            CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellWidth) });
        for (int r = 0; r < rows; r++)
            CellsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellHeight) });

        // One SolidColorBrush per cell — MANDATORY. A brush shared across
        // cells and mutated per tick paints every cell with the grid's last
        // pixel (usually the dark taskbar).
        _cellBrushes = new SolidColorBrush[cols * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var brush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                var cell = new Rectangle { Fill = brush };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                CellsGrid.Children.Add(cell);
                _cellBrushes[r * cols + c] = brush;
            }
        }

        _cols = cols;
        _rows = rows;
        _builtSize = size;
        _stageWidth  = cellWidth * cols;
        _stageHeight = cellHeight * rows;
        ZoneOverlay.Width  = _stageWidth;
        ZoneOverlay.Height = _stageHeight;
    }

    // ── Readouts ─────────────────────────────────────────────────────────

    private void UpdateReadouts(AmbientEngine engine, Windows.Graphics.SizeInt32? size)
    {
        if (size is { Width: > 0, Height: > 0 } s)
        {
            CaptureReadout.Value = Loc.Format("AmbientPreview_Capture_Format",
                s.Width, s.Height,
                Loc.Get(engine.IsHdr ? "AmbientPreview_Hdr" : "AmbientPreview_Sdr"));
        }

        GridReadout.Value = Loc.Format("AmbientPreview_Grid_Format", _cols, _rows);

        OutputReadout.Value = engine.OutputIsLiveStream switch
        {
            true  => Loc.Get("AmbientPreview_Output_Stream"),
            false => Loc.Get("AmbientPreview_Output_Rest"),
            null  => null,
        };

        UpdateRate(engine);
    }

    // Two reads of the engine's frame count at least a window apart. A count
    // that went down means the capture restarted: start a new window.
    private void UpdateRate(AmbientEngine engine)
    {
        long frames = engine.CapturedFrames;
        long now = Stopwatch.GetTimestamp();

        if (_rateTimestamp == 0 || frames < _rateFrames)
        {
            _rateFrames = frames;
            _rateTimestamp = now;
            return;
        }

        double elapsed = (now - _rateTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed < RateWindowSeconds) return;

        double fps = (frames - _rateFrames) / elapsed;
        RateReadout.Value = Loc.Format("AmbientPreview_Rate_Format", Math.Round(fps));
        _rateFrames = frames;
        _rateTimestamp = now;
    }

    // ── Swatches and zones ───────────────────────────────────────────────

    private void UpdateSwatches(AmbientEngine engine, IReadOnlyDictionary<string, LightColor> emitted)
    {
        bool sameSet = emitted.Count == _swatches.Count;
        if (sameSet)
        {
            foreach (var key in emitted.Keys)
                if (!_swatches.ContainsKey(key)) { sameSet = false; break; }
        }

        if (!sameSet)
        {
            EmittedSwatches.Children.Clear();
            _swatches.Clear();
            foreach (var (id, color) in emitted)
            {
                var fill = new SolidColorBrush(ToColor(color));
                var swatch = new Rectangle
                {
                    Width  = 20,
                    Height = 20,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill   = fill,
                    Stroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    StrokeThickness = 1,
                };
                ToolTipService.SetToolTip(swatch, DisplayNameOf(engine, id));
                _swatches[id] = (swatch, fill);
                EmittedSwatches.Children.Add(swatch);
            }
            return;
        }

        foreach (var (id, color) in emitted)
            _swatches[id].Fill.Color = ToColor(color);
    }

    // Tints each band that drives at least one light with that light's
    // emitted colour; bands with no light stay hidden, and every band hides
    // while the Zones command is off. Group mode has no bands: one colour
    // goes to the whole group.
    private void UpdateZoneOverlay(AmbientEngine engine, IReadOnlyDictionary<string, LightColor> emitted)
    {
        var settings = AmbientSettingsService.Instance.Current;
        bool multi = engine.IsMultiLightActive && ZonesToggle.IsChecked == true;

        LayoutZoneRects(settings);
        ApplyZone(LightZone.Top,    ZoneTopRect,    multi, settings, emitted);
        ApplyZone(LightZone.Bottom, ZoneBottomRect, multi, settings, emitted);
        ApplyZone(LightZone.Left,   ZoneLeftRect,   multi, settings, emitted);
        ApplyZone(LightZone.Right,  ZoneRightRect,  multi, settings, emitted);
    }

    private void ApplyZone(
        LightZone zone,
        Rectangle rect,
        bool multi,
        AmbientSettings settings,
        IReadOnlyDictionary<string, LightColor> emitted)
    {
        LightColor? color = null;
        if (multi)
        {
            foreach (var (id, assigned) in settings.LightZones)
            {
                if (assigned == zone && emitted.TryGetValue(id, out var c)) { color = c; break; }
            }
        }

        if (color is null)
        {
            rect.Visibility = Visibility.Collapsed;
            return;
        }

        if (!_zoneBrushes.TryGetValue(zone, out var brush))
        {
            brush = new SolidColorBrush { Opacity = ZoneFillOpacity };
            _zoneBrushes[zone] = brush;
            rect.Fill = brush;
        }
        brush.Color = ToColor(color.Value);
        rect.Visibility = Visibility.Visible;
    }

    // Band thickness follows the engine's own rule
    // (AmbientZoneSampler.ResolveBandCells): a share of the stage in Share
    // mode, a whole number of cells in Cells mode, clamped the same way for
    // a hand-edited file.
    private void LayoutZoneRects(AmbientSettings settings)
    {
        double w = _stageWidth;
        double h = _stageHeight;
        if (w <= 0 || h <= 0 || _cols <= 0 || _rows <= 0) return;

        double bandH, bandV;
        if (settings.BorderMode == BorderThicknessMode.Cells)
        {
            bandH = h * Math.Clamp(settings.BorderCells, 1, _rows) / _rows;
            bandV = w * Math.Clamp(settings.BorderCells, 1, _cols) / _cols;
        }
        else
        {
            double depth = Math.Clamp(settings.BorderDepth, 0.05, 0.5);
            bandH = h * depth;
            bandV = w * depth;
        }

        Place(ZoneTopRect,    0,          0,          w,     bandH);
        Place(ZoneBottomRect, 0,          h - bandH,  w,     bandH);
        Place(ZoneLeftRect,   0,          0,          bandV, h);
        Place(ZoneRightRect,  w - bandV,  0,          bandV, h);
    }

    private static void Place(Rectangle rect, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        rect.Width  = width;
        rect.Height = height;
    }

    private static string DisplayNameOf(AmbientEngine engine, string key)
    {
        if (key == "group") return Loc.Get("AmbientPreview_GroupSwatch");

        if (engine.MultiLights is { } lights)
        {
            foreach (var light in lights)
                if (light.Id == key)
                    return string.IsNullOrWhiteSpace(light.Name) ? key : light.Name;
        }
        return key;
    }

    private static Windows.UI.Color ToColor(LightColor color)
        => Windows.UI.Color.FromArgb(0xFF, color.R, color.G, color.B);
}
