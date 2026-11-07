using System.ComponentModel;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Audio;

// ── VoiceLevelMeter ───────────────────────────────────────────────────────────
//
// The instrument inside the Voice level card on the Recording page: a live
// meter of the microphone on a fixed dB track, with the voice level (the voice
// detector's exit) marked, and the entry level above it when the detector's
// entry margin is positive (with the default 0 they coincide), so the user sets the
// level by eye against their own voice and room. The meter shows and never
// writes: nothing is recorded, and Listen is page state, not a setting.
//
// Under the track, a slider on the meter's own scale writes the voice level,
// the same value as the field in the card's header row (2026-11-07 review,
// change 4): the thumb is the mark, so dragging it shows what passes over the
// level. While the thumb is dragged the marks and the meter's detector follow
// it; the value is written once, at the release (GestureHold, the composer's
// gesture rule), and a keyboard step writes at once.
//
// The bar follows each device packet (CaptureLevel, about 10 ms), with a fast
// rise and a slow fall (VoiceLevelMeterScale.Ballistic), so it moves with the
// voice as it is spoken. The fill takes the accent while the meter's own
// VoiceDetector says voiced, fed the same 50 ms RMS sequence the segmenter
// would be fed, so the colour is the exact decision a dictation would take;
// the peak tick and the Loudest readout read those same 50 ms values, the
// quantity the voice level is compared with. The Signal readout says what
// Windows granted: the raw signal, or its default processing. The marks follow the field as it is
// edited (the ViewModel's VoiceLevelDbfs); the capture is not restarted for it.
// A change of microphone while listening does restart it, on the new device.
//
// Listen does not stay on behind the person's back: it turns itself off after
// ListenTimeout, when the window is hidden or minimized, when the page is left,
// and when the page's microphone check or listening test needs the device.
//
// Threading. The capture thread (MicrophoneLevelFeed) delivers each packet's
// level to OnPulse (the bar) and each 50 ms frame's RMS to OnLevel (the
// detector and the peak), both under _levelLock; the UI thread replaces the
// detector under the same lock when the level changes. The paint runs on
// CompositionTarget.Rendering, once per frame the compositor draws, only while
// listening: it reads the latest values under the lock, so the bar is at most
// one display frame behind the packet. The readouts' text changes at
// ReadoutInterval, slow enough to be read. A failure, and the source once the
// device is open, arrive on the capture thread and are marshalled to the UI
// thread, where a run number drops what belongs to a run already stopped.
public sealed partial class VoiceLevelMeter : UserControl
{
    // How often the readouts' figures change: a number rewritten at the display
    // rate cannot be read.
    private const double ReadoutInterval = 0.1;

    // A meter is looked at, not left on: past this, Listen turns itself off and
    // says so, so a microphone opened to set a level does not stay open for
    // the rest of the session. The RecordingVoiceMeter_TimedOut string names
    // the same duration.
    internal static readonly TimeSpan ListenTimeout = TimeSpan.FromMinutes(2);

    // Space between a mark and its caption.
    private const double CaptionGap = 4;

    private bool _painting;
    private double _readoutsAt = double.NegativeInfinity;
    private readonly DispatcherTimer _listenTimer = new() { Interval = ListenTimeout };
    private readonly MicrophoneLevelFeed _feed;

    // Captured on the UI thread when the feed is built: the failure sink runs
    // on the capture thread, where the control's own DispatcherQueue property
    // must not be read, and may run after the control is torn down.
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    // The host of the next run, built by StartListening and read by the feed's
    // Start on the same thread.
    private VoiceLevelMeterHost? _nextHost;
    private XamlRoot? _xamlRoot;

    // Shared with the capture thread, guarded by _levelLock.
    private readonly object _levelLock = new();
    private VoiceDetector _detector = new(new CaptureSettings().VoiceLevelDbfs);
    private double? _barDbfs;
    private bool _voiced;
    private double _framePeakDbfs = double.NegativeInfinity;

    // UI thread only.
    private readonly Queue<(double AtSeconds, double Dbfs)> _recent = new();
    private RecordingViewModel? _viewModel;
    private int _run;
    private double _exitDbfs;
    private double _entryDbfs;
    private double _peakDbfs = double.NegativeInfinity;
    private double _peakHeldSince;
    private double _canvasWidth;

    // The slider's gesture and the guard that keeps a programmatic Value set
    // (the model moving it, or the coercion of a bound change) from writing.
    private readonly GestureHold _sliderHold = new();
    private bool _syncingSlider;

    public VoiceLevelMeter()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue;
        _feed = new MicrophoneLevelFeed(
            createCapture: () => new MicrophoneCapture(),
            host: () => _nextHost!,
            onLevel: OnLevel,
            onFailed: OnFeedFailed,
            onPulse: OnPulse,
            onSource: OnFeedSource);
        _listenTimer.Tick += (_, _) => OnListenTimedOut();
        Loaded += (_, _) => WatchHostVisibility();
        Unloaded += (_, _) =>
        {
            UnwatchHostVisibility();
            Stop();
        };

        ScaleMinText.Text = FormatDbfs(VoiceLevelMeterScale.MinDbfs);
        ScaleMaxText.Text = FormatDbfs(VoiceLevelMeterScale.MaxDbfs);
        ConfigureSlider();
        ApplyThumbInset();
        RebuildDetector();
    }

    // ── Slider ────────────────────────────────────────────────────────────────

    private void ConfigureSlider()
    {
        _syncingSlider = true;
        try
        {
            LevelSlider.Minimum = VoiceLevelMeterScale.MinDbfs;
            LevelSlider.Maximum = VoiceLevelMeterScale.MaxDbfs;
            LevelSlider.StepFrequency = VoiceLevelMeterScale.SliderStepDb;
            LevelSlider.SmallChange = VoiceLevelMeterScale.SliderStepDb;
            LevelSlider.LargeChange = VoiceLevelMeterScale.SliderStepDb * 5;
        }
        finally { _syncingSlider = false; }

        // The Thumb takes the press and captures the pointer, so the slider sees
        // these only with handledEventsToo. Release, capture loss and cancel all
        // end the gesture, so the hold cannot stay armed once the pointer is gone.
        LevelSlider.AddHandler(PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _sliderHold.Begin()), handledEventsToo: true);
        var endGesture = new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            if (_sliderHold.End() is double held) WriteVoiceLevel(held);
        });
        LevelSlider.AddHandler(PointerReleasedEvent, endGesture, handledEventsToo: true);
        LevelSlider.AddHandler(PointerCaptureLostEvent, endGesture, handledEventsToo: true);
        LevelSlider.AddHandler(PointerCanceledEvent, endGesture, handledEventsToo: true);
    }

    // The thumb's centre travels from half a thumb inside the slider's left edge
    // to half a thumb inside its right edge; the scale row, the track and the
    // captions take the same inset, so a value sits at one x on all of them.
    // The width is the theme's own (SliderHorizontalThumbWidth), read rather
    // than copied; 18, its WinUI value, only if the resource is missing.
    private void ApplyThumbInset()
    {
        double thumb = 18.0;
        try
        {
            // The indexer searches the merged XamlControlsResources, where the
            // theme's slider metrics live; a missing key throws.
            if (Application.Current.Resources["SliderHorizontalThumbWidth"] is double width) thumb = width;
        }
        catch (Exception) { }
        var inset = new Thickness(thumb / 2, 0, thumb / 2, 0);
        ScaleRow.Margin = inset;
        MeterCanvas.Margin = inset;
        CaptionCanvas.Margin = inset;
    }

    private void LevelSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSlider) return;
        // The marks and the meter's own detector follow the thumb at once; the
        // model waits for the gesture's end.
        ShowLevel(e.NewValue);
        if (_sliderHold.Offer(e.NewValue)) WriteVoiceLevel(e.NewValue);
    }

    private void WriteVoiceLevel(double dbfs)
    {
        if (_viewModel is null || _viewModel.VoiceLevelDbfs == dbfs) return;
        _viewModel.VoiceLevelDbfs = dbfs;
    }

    // The thumb follows the model, clamped to the scale by the slider itself;
    // the guard keeps the coerced value from writing back.
    private void SyncSlider(double dbfs)
    {
        _syncingSlider = true;
        try
        {
            if (LevelSlider.Value != dbfs) LevelSlider.Value = dbfs;
        }
        finally { _syncingSlider = false; }
    }

    // The page's ViewModel: the voice level the marks follow and the microphone
    // the meter opens. A plain property, set once by the page after
    // InitializeComponent; the two live and die with the cached page.
    public RecordingViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RebuildDetector();
        }
    }

    // Closes the microphone and clears the meter. Called on Unloaded, by the
    // page when it is navigated away from or needs the microphone, when the
    // window is hidden, on the idle timeout, and when the capture ends.
    // The feed is stopped before the run moves on: a failure sink, which runs
    // under the feed's lock, then either read the old run number (and is
    // dropped on the UI thread) or never runs.
    public void Stop()
    {
        _feed.Stop();
        Interlocked.Increment(ref _run);
        StopPainting();
        _listenTimer.Stop();
        ListenToggle.IsChecked = false;
        Block.IsLive = false;
        ClearLive();
    }

    private void ListenToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ListenToggle.IsChecked == true) StartListening();
        else Stop();
    }

    private void StartListening()
    {
        FailureBar.IsOpen = false;
        ClearLive();

        var capture = CaptureSettingsService.Instance.Current;
        string device = _viewModel?.AudioInputEndpointId ?? capture.AudioInputEndpointId;
        _nextHost = new VoiceLevelMeterHost(device, capture.VoiceLevelDbfs);
        _feed.Start();

        ListenToggle.IsChecked = true;
        Block.IsLive = true;
        StartPainting();
        _listenTimer.Start();
    }

    private void StartPainting()
    {
        if (_painting) return;
        _painting = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopPainting()
    {
        if (!_painting) return;
        _painting = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e) => Refresh();

    // Capture thread, under the feed's lock, once the device is open.
    private void OnFeedSource(CaptureSource source)
    {
        int run = Volatile.Read(ref _run);
        _dispatcher.TryEnqueueObserved(
            operation: "ui-update", caller: "voice-level-meter",
            callback: () =>
            {
                if (run != _run) return;
                SignalReadout.Value = Loc.Get(source.Mode == CaptureMode.Raw
                    ? "RecordingVoiceMeter_Signal_Raw"
                    : "RecordingVoiceMeter_Signal_Default");
                SignalReadout.Status = source.Mode == CaptureMode.Raw ? ReadoutStatus.Neutral : ReadoutStatus.Caution;
            },
            rejectSource: "SETTINGS", rejectWhat: "voice meter source");
    }

    // Capture thread, under the feed's lock: the run number read here is the
    // failed run's, since Stop moves it only once the feed is stopped.
    private void OnFeedFailed(MicrophoneLevelFeedFailure failure)
    {
        int run = Volatile.Read(ref _run);
        _dispatcher.TryEnqueueObserved(
            operation: "ui-update", caller: "voice-level-meter",
            callback: () => OnFailed(run, failure),
            rejectSource: "SETTINGS", rejectWhat: "voice meter failure");
    }

    private void OnFailed(int run, MicrophoneLevelFeedFailure failure)
    {
        if (run != _run) return;
        Stop();
        ShowNote(InfoBarSeverity.Warning, failure.Problem switch
        {
            MicrophoneProblem.NotDetected => "RecordingVoiceMeter_Failure_NotDetected",
            MicrophoneProblem.InUse       => "RecordingVoiceMeter_Failure_InUse",
            MicrophoneProblem.Ended       => "RecordingVoiceMeter_Failure_Ended",
            _                             => "RecordingVoiceMeter_Failure_Unavailable",
        });
    }

    private void OnListenTimedOut()
    {
        if (ListenToggle.IsChecked != true) return;
        Stop();
        ShowNote(InfoBarSeverity.Informational, "RecordingVoiceMeter_TimedOut");
    }

    // Why the microphone closed when the person did not close it.
    private void ShowNote(InfoBarSeverity severity, string key)
    {
        FailureBar.Severity = severity;
        FailureBar.Title = Loc.Get(key);
        FailureBar.IsOpen = true;
    }

    // ── Window visibility ─────────────────────────────────────────────────────
    //
    // A hidden or minimized window shows no meter, so Listen stops: the
    // XamlRoot's IsHostVisible follows the window, and its Changed event fires
    // when it flips. Watched while the control is loaded only. Coming back
    // does not reopen the microphone; the person turns Listen on again.

    private void WatchHostVisibility()
    {
        UnwatchHostVisibility();
        _xamlRoot = XamlRoot;
        if (_xamlRoot is not null) _xamlRoot.Changed += OnXamlRootChanged;
    }

    private void UnwatchHostVisibility()
    {
        if (_xamlRoot is not null) _xamlRoot.Changed -= OnXamlRootChanged;
        _xamlRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible && ListenToggle.IsChecked == true) Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordingViewModel.VoiceLevelDbfs):
                RebuildDetector();
                break;
            case nameof(RecordingViewModel.AudioInputEndpointId) when ListenToggle.IsChecked == true:
                Stop();
                StartListening();
                break;
        }
    }

    // ── Capture thread ────────────────────────────────────────────────────────

    // Each 50 ms frame: the decision and the peak, on the segmenter's quantity.
    private void OnLevel(float rms)
    {
        double dbfs = VoiceLevelMeterScale.RmsToDbfs(rms);
        lock (_levelLock)
        {
            _voiced = _detector.Push(rms);
            if (dbfs > _framePeakDbfs) _framePeakDbfs = dbfs;
        }
    }

    // Each device packet: the bar.
    private void OnPulse(CaptureLevel level)
    {
        double dbfs = VoiceLevelMeterScale.RmsToDbfs(level.Rms);
        lock (_levelLock)
        {
            _barDbfs = VoiceLevelMeterScale.Ballistic(_barDbfs, dbfs, level.DurationMs);
        }
    }

    // ── Detector and marks ────────────────────────────────────────────────────

    // A detector on the current voice level, its two levels for the marks, and
    // the thumb on the same value. The new detector starts silent, like a
    // dictation's; the next frame decides. A cleared field (NaN) keeps the
    // marks where they were.
    private void RebuildDetector()
    {
        double level = _viewModel?.VoiceLevelDbfs ?? CaptureSettingsService.Instance.Current.VoiceLevelDbfs;
        if (double.IsNaN(level)) return;

        SyncSlider(level);
        ShowLevel(level);
    }

    // The marks and the meter's detector on a level, the model's or the one
    // under a thumb being dragged.
    private void ShowLevel(double level)
    {
        var detector = new VoiceDetector(level);
        lock (_levelLock)
        {
            _detector = detector;
            _voiced = false;
        }
        _exitDbfs = detector.ExitDbfs;
        _entryDbfs = detector.EntryDbfs;
        EntryCaption.Text = Loc.Format("RecordingVoiceMeter_EntryCaption_Format",
            (int)Math.Round(detector.EntryDbfs - detector.ExitDbfs));

        // With the default margin of 0 the entry is the voice level itself: a
        // second mark on top of the first, captioned "+0 dB", would only say
        // there is a band where there is none.
        var entryVisibility = detector.EntryDbfs == detector.ExitDbfs
            ? Visibility.Collapsed
            : Visibility.Visible;
        EntryMark.Visibility = entryVisibility;
        EntryCaption.Visibility = entryVisibility;
        LayoutMarks();
    }

    private void MeterCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _canvasWidth = e.NewSize.Width;
        LayoutMarks();
    }

    private void Caption_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCaptions();

    private void LayoutMarks()
    {
        double w = _canvasWidth;
        if (w <= 0) return;

        TrackRect.Width = w;

        double xExit = w * VoiceLevelMeterScale.Fraction(_exitDbfs);
        double xEntry = w * VoiceLevelMeterScale.Fraction(_entryDbfs);

        Canvas.SetLeft(VoiceBandRect, xExit);
        VoiceBandRect.Width = Math.Max(0, w - xExit);

        Canvas.SetLeft(VoiceMark, Math.Clamp(xExit - VoiceMark.Width / 2, 0, Math.Max(0, w - VoiceMark.Width)));

        double xLine = Math.Clamp(xEntry, 0.5, Math.Max(0.5, w - 0.5));
        EntryMark.X1 = xLine;
        EntryMark.X2 = xLine;

        LayoutCaptions();
    }

    private void LayoutCaptions()
    {
        double w = _canvasWidth;
        if (w <= 0) return;

        double xExit = w * VoiceLevelMeterScale.Fraction(_exitDbfs);
        double xEntry = w * VoiceLevelMeterScale.Fraction(_entryDbfs);

        double voiceWidth = VoiceCaption.ActualWidth;
        Canvas.SetLeft(VoiceCaption, Math.Clamp(xExit - CaptionGap - voiceWidth, 0, Math.Max(0, w - voiceWidth)));

        double entryWidth = EntryCaption.ActualWidth;
        Canvas.SetLeft(EntryCaption, Math.Clamp(xEntry + CaptionGap, 0, Math.Max(0, w - entryWidth)));
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    private void Refresh()
    {
        double? bar;
        bool voiced;
        double framePeak;
        lock (_levelLock)
        {
            bar = _barDbfs;
            voiced = _voiced;
            framePeak = _framePeakDbfs;
            _framePeakDbfs = double.NegativeInfinity;
        }

        // Nothing yet from the microphone: the meter stays empty.
        if (bar is not { } level) return;

        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (!double.IsNegativeInfinity(framePeak))
        {
            (_peakDbfs, _peakHeldSince) = VoiceLevelMeterScale.NextPeak(_peakDbfs, _peakHeldSince, framePeak, now);
            _recent.Enqueue((now, framePeak));
        }
        while (_recent.Count > 0 && now - _recent.Peek().AtSeconds > VoiceLevelMeterScale.LoudestWindowSeconds)
            _recent.Dequeue();

        PaintBar(level, voiced);

        if (now - _readoutsAt < ReadoutInterval) return;
        _readoutsAt = now;
        LevelReadout.Value = FormatDbfs(level);
        VoiceReadout.Value = Loc.Get(voiced ? "RecordingVoiceMeter_Voice" : "RecordingVoiceMeter_Silence");
        VoiceReadout.Status = voiced ? ReadoutStatus.Success : ReadoutStatus.Neutral;
        LoudestReadout.Value = _recent.Count > 0 ? FormatDbfs(_recent.Max(r => r.Dbfs)) : null;
    }

    private void PaintBar(double level, bool voiced)
    {
        double w = _canvasWidth;
        if (w <= 0) return;

        double fill = w * VoiceLevelMeterScale.Fraction(level);
        VoicedFillRect.Width = fill;
        SilentFillRect.Width = fill;
        VoicedFillRect.Visibility = voiced ? Visibility.Visible : Visibility.Collapsed;
        SilentFillRect.Visibility = voiced ? Visibility.Collapsed : Visibility.Visible;

        if (double.IsNegativeInfinity(_peakDbfs))
        {
            PeakRect.Visibility = Visibility.Collapsed;
            return;
        }
        double xPeak = w * VoiceLevelMeterScale.Fraction(_peakDbfs);
        Canvas.SetLeft(PeakRect, Math.Clamp(xPeak - PeakRect.Width / 2, 0, Math.Max(0, w - PeakRect.Width)));
        PeakRect.Visibility = Visibility.Visible;
    }

    // Nothing stale between two runs: the smoothing, the detector's state, the
    // peak, the window and the readouts start over.
    private void ClearLive()
    {
        lock (_levelLock)
        {
            _barDbfs = null;
            _voiced = false;
            _framePeakDbfs = double.NegativeInfinity;
            _detector.Reset();
        }
        _peakDbfs = double.NegativeInfinity;
        _peakHeldSince = 0;
        _recent.Clear();
        _readoutsAt = double.NegativeInfinity;

        VoicedFillRect.Width = 0;
        SilentFillRect.Width = 0;
        VoicedFillRect.Visibility = Visibility.Collapsed;
        SilentFillRect.Visibility = Visibility.Visible;
        PeakRect.Visibility = Visibility.Collapsed;

        LevelReadout.Value = null;
        VoiceReadout.Value = null;
        VoiceReadout.Status = ReadoutStatus.None;
        LoudestReadout.Value = null;
        SignalReadout.Value = null;
        SignalReadout.Status = ReadoutStatus.None;
    }

    // Rounded to the dB, as integers so a level just under zero never reads -0.
    private static string FormatDbfs(double dbfs)
        => Loc.Format("RecordingVoiceMeter_Dbfs_Format", (int)Math.Round(dbfs));
}
