using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Deckle.Catalog;
using Deckle.Lighting;
using Deckle.Shell;
using Deckle.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Deckle.Lighting.Ambient;

// Live preview of the engine in the shared PreviewBlock, floating in the
// page's pane: the sampled grid mirroring the screen as the stage, the colour
// each light receives as swatches beside the commands, the four zone bands
// over the stage, and the capture's format, grid, measured cadence and output
// transport as readouts under it.
//
// Three sources, the richest available wins:
//   - Ambient lighting starting or running: the run's own capture, the
//     swatches and the bands tinted with what each light receives. The
//     cells show from the first sampled frame, during the bridge handshake
//     too, not only once the run is Running.
//   - Ambient lighting off, pane open: the engine's preview scan, the same
//     capture with no output (AmbientEngine.PreviewScan.cs). Cells and
//     readouts are live, no light receives anything, the bands are drawn
//     neutral.
//   - No capture (the scan is starting, failed or was lost): the display's
//     frame, its ratio read from Windows (AmbientCaptureTarget), with the
//     grid the capture would sample and the neutral bands over it.
// So the pane never opens on an empty sentence (Louis, 2026-11-07: "a dead
// end"); the block's empty text remains only for a machine with no display
// to read.
//
// The pane's owner says when the preview is on screen (IsActive): the scan
// and the 200 ms refresh run only then, and only while the control is
// loaded; the refresh also waits while the user has paused it. The pane
// around the control is the page's: Close only asks the page to close it
// (CloseRequested).
//
// Ratio: each cell is sourceWidth / cols by sourceHeight / rows in source
// pixels, and a uniform Viewbox scales the stage, so the preview has the
// display's exact shape.
//
// Cadence: the engine counts the frames its capture delivers; two reads a
// second or more apart give the rate the capture really holds, which is what
// the readout shows — never the rate the user picked.
//
// Threading: AmbientEngine.StateChanged and AmbientSettingsService.Changed
// fire off the UI thread and are marshalled to it; everything else runs on
// the UI thread.
public sealed partial class AmbientPreviewCard : UserControl
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(200);

    // Soft fill so the cells stay readable through a zone band. Opacity on
    // the brush, not the rectangle, so a future stroke keeps full opacity.
    private const double ZoneFillOpacity = 0.4;

    private const double RateWindowSeconds = 1.0;

    // Under this width the readouts stack in one column (the NarrowReadouts
    // state in the XAML). The control's width is the pane's minus the page's
    // 32 px padding on each side, so 240 here is a pane of about 300 px
    // (Louis, 2026-10-15), and the pane's 240 px floor always stacks.
    private const double NarrowReadoutsWidth = 240;

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly Dictionary<string, (Rectangle Swatch, SolidColorBrush Fill)> _swatches = new();
    private readonly Dictionary<LightZone, SolidColorBrush> _zoneBrushes = new();

    private AmbientEngine? _engine;
    private bool _isActive;
    private bool _scanRequestInFlight;

    // Set when a scan was refused or ended on its own (the capture failed or
    // was lost): the preview keeps the display's frame and does not ask
    // again until something changes, the engine's state, the pane, the
    // display or the rate. Without it a failing capture would be retried on
    // every state update.
    private bool _scanHeld;

    // What the running scan was started with; a change restarts it, so the
    // preview follows the display and rate the run would use.
    private string? _scanMonitor;
    private int _scanRateHz;

    private SolidColorBrush[]? _cellBrushes;
    private int _cols;
    private int _rows;
    private Windows.Graphics.SizeInt32? _builtSize;
    private double _stageWidth;
    private double _stageHeight;
    private bool _cellsShowCapture;
    private long _rateFrames;
    private long _rateTimestamp;

    // Raised by the Close command: the page that holds the control closes the
    // pane.
    public event RoutedEventHandler? CloseRequested;

    public AmbientPreviewCard()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>True while the pane holding the preview is open. The page
    /// sets it; the scan and the refresh run only while it is true.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            _scanHeld = false;
            UpdateState();
        }
    }

    // The readouts' layout follows the control's own width, which the pane's
    // splitter changes: AdaptiveTrigger would watch the window instead. No
    // transition: the states only move elements between grid cells.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => VisualStateManager.GoToState(
            this,
            e.NewSize.Width < NarrowReadoutsWidth ? "NarrowReadouts" : "WideReadouts",
            useTransitions: false);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _engine = AmbientEngine.Current;
        if (_engine is not null) _engine.StateChanged += OnEngineStateChanged;
        AmbientSettingsService.Instance.Changed += OnSettingsChanged;
        UpdateState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_engine is not null)
        {
            _engine.StateChanged -= OnEngineStateChanged;
            _ = _engine.StopPreviewScanAsync();
        }
        AmbientSettingsService.Instance.Changed -= OnSettingsChanged;
        _engine = null;
        _timer.Stop();
        PauseToggle.IsChecked = false;
    }

    private void OnEngineStateChanged(AmbientEngineState state)
        => DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "ambient-preview-card",
            callback: () =>
            {
                _scanHeld = false;
                UpdateState();
            },
            rejectSource: "AMBIENT", rejectWhat: "preview state");

    private void OnSettingsChanged()
        => DispatcherQueue.TryEnqueueObserved(
            operation: "settings-reload", caller: "ambient-preview-card",
            callback: OnSettingsChangedOnUi,
            rejectSource: "AMBIENT", rejectWhat: "preview settings");

    // A new display or capture rate restarts the scan; any other change
    // (zone mode, band depth) only redraws.
    private void OnSettingsChangedOnUi()
    {
        var settings = AmbientSettingsService.Instance.Current;
        if (_engine is { IsPreviewScanning: true } engine
            && (!string.Equals(settings.SelectedMonitorDeviceName, _scanMonitor, StringComparison.Ordinal)
                || AmbientCaptureRate.Snap(settings.CaptureFrameRateHz) != _scanRateHz))
        {
            _ = RestartScanAsync(engine);
            return;
        }

        UpdateState();
    }

    private void ZonesToggle_Click(object sender, RoutedEventArgs e) => UpdateZoneOverlay(Emitted());

    private void PauseToggle_Click(object sender, RoutedEventArgs e) => UpdateState();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    // ── State ────────────────────────────────────────────────────────────

    // The run's capture feeds the cells from Starting on, as soon as its
    // sampler has a frame.
    private static bool RunCaptures(AmbientEngine engine)
        => engine.State is AmbientEngineState.Starting or AmbientEngineState.Running;

    private bool HasCapture(AmbientEngine? engine)
        => engine is not null && (RunCaptures(engine) || engine.IsPreviewScanning);

    private void UpdateState()
    {
        var engine = _engine;
        bool shown = _isActive && IsLoaded;

        if (engine is not null)
        {
            if (shown && !_scanHeld && engine.State == AmbientEngineState.Off && !engine.IsPreviewScanning)
                _ = RequestScanAsync(engine);
            else if (!shown && engine.IsPreviewScanning)
                _ = engine.StopPreviewScanAsync();
        }

        bool capturing = HasCapture(engine);
        PauseToggle.IsEnabled = capturing;
        if (!capturing) PauseToggle.IsChecked = false;
        if (capturing) Block.IsLive = true;

        bool paused = PauseToggle.IsChecked == true;
        if (shown && capturing && !paused)
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
            // A pause keeps the frame it froze; anything else without a
            // capture falls back to the display's frame.
            if (!capturing) ShowDisplayFrame();
        }
    }

    // Fire and forget from the UI thread: the scan's capture starts off it,
    // and the continuation comes back here to start the refresh.
    private async Task RequestScanAsync(AmbientEngine engine)
    {
        if (_scanRequestInFlight) return;
        _scanRequestInFlight = true;
        try
        {
            var settings = AmbientSettingsService.Instance.Current;
            _scanMonitor = settings.SelectedMonitorDeviceName;
            _scanRateHz = AmbientCaptureRate.Snap(settings.CaptureFrameRateHz);
            if (!await engine.StartPreviewScanAsync()) _scanHeld = true;
        }
        finally
        {
            _scanRequestInFlight = false;
        }

        // The pane may have closed while the capture started.
        if (!ReferenceEquals(engine, _engine) || !_isActive || !IsLoaded)
        {
            _ = engine.StopPreviewScanAsync();
            return;
        }
        UpdateState();
    }

    private async Task RestartScanAsync(AmbientEngine engine)
    {
        await engine.StopPreviewScanAsync(AmbientPreviewScanEndReasons.SettingsChanged);
        _scanHeld = false;
        if (ReferenceEquals(engine, _engine)) UpdateState();
    }

    // ── Refresh ──────────────────────────────────────────────────────────

    private void Refresh()
    {
        var engine = _engine;
        if (!HasCapture(engine))
        {
            // The timer runs only while a capture runs, and the preview's own
            // stops halt it first: a scan gone from under it ended on its own.
            if (engine is { State: AmbientEngineState.Off }) _scanHeld = true;
            UpdateState();
            return;
        }

        // Between the start of a capture and its first sampled frame, the
        // stage keeps what it drew: the display's frame, or the scan's last
        // frame while the run takes the display over.
        var sample = engine!.LatestSample;
        var size = engine.SourceSize;
        if (sample is null)
        {
            if (_cellBrushes is null) DrawDisplayFrame();
            UpdateZoneOverlay(Emitted());
            return;
        }

        if (_cellBrushes is null || sample.Cols != _cols || sample.Rows != _rows || size != _builtSize)
            BuildGrid(sample.Cols, sample.Rows, size);

        // One brush per cell (see BuildGrid): writing the colour in place.
        int total = Math.Min(sample.Grid.Length, _cellBrushes!.Length);
        for (int i = 0; i < total; i++)
            _cellBrushes[i].Color = sample.Grid[i];
        _cellsShowCapture = true;

        var emitted = Emitted();
        UpdateSwatches(engine, emitted);
        UpdateZoneOverlay(emitted);
        UpdateReadouts(engine, size);
    }

    // The colours the lights receive: only a run sends any.
    private IReadOnlyDictionary<string, LightColor> Emitted()
        => _engine is { IsRunning: true } engine
            ? engine.SnapshotEmittedColors()
            : new Dictionary<string, LightColor>();

    // No capture: the display's frame, the grid its capture would sample, the
    // bands over it, and the readouts that do not need a capture. Nothing of
    // a past capture stays on screen.
    private void ShowDisplayFrame()
    {
        var display = DrawDisplayFrame();
        if (display is null)
        {
            Block.IsLive = false;
            ClearReadouts();
            return;
        }

        if (_cellsShowCapture) ClearCells();

        Block.IsLive = true;
        EmittedSwatches.Children.Clear();
        _swatches.Clear();
        UpdateZoneOverlay(Emitted());

        CaptureReadout.Value = Loc.Format("AmbientPreview_Display_Format", display.Width, display.Height);
        GridReadout.Value = Loc.Format("AmbientPreview_Grid_Format", _cols, _rows);
        RateReadout.Value = null;
        OutputReadout.Value = Loc.Get("AmbientPreview_Output_Off");
    }

    // Sizes the stage on the display the capture targets, read from Windows,
    // with the grid its capture would sample. Keeps a grid already built for
    // that size and shape. Null when Windows reports no display.
    private CaptureMonitor? DrawDisplayFrame()
    {
        var settings = AmbientSettingsService.Instance.Current;
        var display = AmbientCaptureTarget.Resolve(
            ScreenCaptureService.GetAvailableMonitors(), settings.SelectedMonitorDeviceName);
        if (display is null) return null;

        var size = new Windows.Graphics.SizeInt32 { Width = display.Width, Height = display.Height };
        var (cols, rows) = FrameSampler.GridFor(display.Width, display.Height);
        if (_cellBrushes is null || cols != _cols || rows != _rows || size != _builtSize)
            BuildGrid(cols, rows, size);
        return display;
    }

    private void ClearCells()
    {
        if (_cellBrushes is not null)
        {
            foreach (var brush in _cellBrushes)
                brush.Color = Microsoft.UI.Colors.Transparent;
        }
        _cellsShowCapture = false;
    }

    private void ClearReadouts()
    {
        EmittedSwatches.Children.Clear();
        _swatches.Clear();
        CaptureReadout.Value = null;
        GridReadout.Value = null;
        RateReadout.Value = null;
        OutputReadout.Value = null;
    }

    private void BuildGrid(int cols, int rows, Windows.Graphics.SizeInt32? size)
    {
        CellsGrid.RowDefinitions.Clear();
        CellsGrid.ColumnDefinitions.Clear();
        CellsGrid.Children.Clear();

        // Without a source size the cells fall back to the grid's own shape,
        // one unit each; every caller has one today.
        double cellWidth  = size is { Width: > 0 } s1 ? (double)s1.Width / cols : 1;
        double cellHeight = size is { Height: > 0 } s2 ? (double)s2.Height / rows : 1;

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
        _cellsShowCapture = false;
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

        // The scan sends nothing: Off, never a transport.
        OutputReadout.Value = engine.IsPreviewScanning
            ? Loc.Get("AmbientPreview_Output_Off")
            : engine.OutputIsLiveStream switch
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

    // The bands show in Colour per zone mode while the Zones command is on;
    // group mode has none, one colour goes to the whole group. During a run,
    // the mode is the one the run resolved (a group with no addressable
    // light falls back to group mode), and each band that drives a light is
    // tinted with that light's colour, the others hidden. With Ambient off,
    // the mode is the setting and all four bands are drawn neutral, so the
    // layout and the band depth read without a light (Louis, 2026-11-07).
    private void UpdateZoneOverlay(IReadOnlyDictionary<string, LightColor> emitted)
    {
        var engine = _engine;
        var settings = AmbientSettingsService.Instance.Current;
        bool running = engine is { IsRunning: true };
        bool zoned = running ? engine!.IsMultiLightActive : settings.UseMultiLight;

        ZonesToggle.IsEnabled = zoned;
        bool show = zoned && ZonesToggle.IsChecked == true;

        LayoutZoneRects(settings);
        ApplyZone(LightZone.Top,    ZoneTopRect,    show, running, settings, emitted);
        ApplyZone(LightZone.Bottom, ZoneBottomRect, show, running, settings, emitted);
        ApplyZone(LightZone.Left,   ZoneLeftRect,   show, running, settings, emitted);
        ApplyZone(LightZone.Right,  ZoneRightRect,  show, running, settings, emitted);
    }

    private void ApplyZone(
        LightZone zone,
        Rectangle rect,
        bool show,
        bool running,
        AmbientSettings settings,
        IReadOnlyDictionary<string, LightColor> emitted)
    {
        if (!show)
        {
            rect.Visibility = Visibility.Collapsed;
            return;
        }

        if (!running)
        {
            rect.Fill = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"];
            rect.Visibility = Visibility.Visible;
            return;
        }

        LightColor? color = null;
        foreach (var (id, assigned) in settings.LightZones)
        {
            if (assigned == zone && emitted.TryGetValue(id, out var c)) { color = c; break; }
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
        }
        brush.Color = ToColor(color.Value);
        rect.Fill = brush;
        rect.Visibility = Visibility.Visible;
    }

    // The bands the push loop averages, in whole cells of the stage's grid
    // (AmbientZoneSampler.ResolveBandShares), so a band drawn here is the
    // band a light is fed from.
    private void LayoutZoneRects(AmbientSettings settings)
    {
        double w = _stageWidth;
        double h = _stageHeight;
        if (w <= 0 || h <= 0 || _cols <= 0 || _rows <= 0) return;

        var (topBottom, leftRight) = AmbientZoneSampler.ResolveBandShares(
            settings.BorderMode, settings.BorderDepth, settings.BorderCells, _cols, _rows);
        double bandH = h * topBottom;
        double bandV = w * leftRight;

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
