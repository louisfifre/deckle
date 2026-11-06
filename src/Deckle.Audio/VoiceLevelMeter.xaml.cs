using System.ComponentModel;
using System.Diagnostics;
using Deckle.Catalog;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Audio;

// ── VoiceLevelMeter ───────────────────────────────────────────────────────────
//
// The instrument beside the Voice level field on the Recording page: a live
// meter of the microphone on a fixed dB track, with the voice level (the voice
// detector's exit) marked, and the entry level above it when the detector's
// entry margin is positive (with the default 0 they coincide), so the user sets the
// level by eye against their own voice and room. It shows and never writes:
// nothing is recorded, nothing persists, and Listen is page state, not a
// setting.
//
// The fill takes the accent while the meter's own VoiceDetector says voiced,
// fed the same RMS sequence the segmenter would be fed, so the colour is the
// exact decision a dictation would take. The marks follow the field as it is
// edited (the ViewModel's VoiceLevelDbfs); the capture is not restarted for it.
// A change of microphone while listening does restart it, on the new device.
//
// Threading. The capture thread (VoiceLevelMeterFeed) delivers each frame's RMS
// to OnLevel, which feeds the detector and the smoothing under _levelLock; the
// UI thread replaces the detector under the same lock when the level changes.
// A DispatcherTimer at the capture's own 50 ms cadence reads the latest values
// under the lock and paints; it runs only while listening. A failure arrives on
// the capture thread and is marshalled to the UI thread, where a run number
// drops one that belongs to a run already stopped.
public sealed partial class VoiceLevelMeter : UserControl
{
    // The capture's own cadence: one paint per frame.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50);

    // Space between a mark and its caption.
    private const double CaptionGap = 4;

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly VoiceLevelMeterFeed _feed;

    // Shared with the capture thread, guarded by _levelLock.
    private readonly object _levelLock = new();
    private VoiceDetector _detector = new(new CaptureSettings().VoiceLevelDbfs);
    private double? _smoothedDbfs;
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

    public VoiceLevelMeter()
    {
        InitializeComponent();
        _feed = new VoiceLevelMeterFeed(() => new MicrophoneCapture(), OnLevel);
        _timer.Tick += (_, _) => Refresh();
        Unloaded += (_, _) => Stop();

        ScaleMinText.Text = FormatDbfs(VoiceLevelMeterScale.MinDbfs);
        ScaleMaxText.Text = FormatDbfs(VoiceLevelMeterScale.MaxDbfs);
        RebuildDetector();
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
    // page when it is navigated away from, and when the capture ends.
    public void Stop()
    {
        _run++;
        _feed.Stop();
        _timer.Stop();
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

        int run = ++_run;
        var capture = CaptureSettingsService.Instance.Current;
        int device = _viewModel?.AudioInputDeviceId ?? capture.AudioInputDeviceId;
        _feed.Start(device, capture.VoiceLevelDbfs, failure => DispatcherQueue.TryEnqueueObserved(
            operation: "ui-update", caller: "voice-level-meter",
            callback: () => OnFailed(run, failure),
            rejectSource: "SETTINGS", rejectWhat: "voice meter failure"));

        ListenToggle.IsChecked = true;
        Block.IsLive = true;
        _timer.Start();
    }

    private void OnFailed(int run, VoiceLevelMeterFailure failure)
    {
        if (run != _run) return;
        Stop();
        FailureBar.Title = Loc.Get(failure switch
        {
            VoiceLevelMeterFailure.NotDetected => "RecordingVoiceMeter_Failure_NotDetected",
            VoiceLevelMeterFailure.InUse       => "RecordingVoiceMeter_Failure_InUse",
            VoiceLevelMeterFailure.Ended       => "RecordingVoiceMeter_Failure_Ended",
            _                                  => "RecordingVoiceMeter_Failure_Unavailable",
        });
        FailureBar.IsOpen = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordingViewModel.VoiceLevelDbfs):
                RebuildDetector();
                break;
            case nameof(RecordingViewModel.AudioInputDeviceId) when ListenToggle.IsChecked == true:
                Stop();
                StartListening();
                break;
        }
    }

    // ── Capture thread ────────────────────────────────────────────────────────

    private void OnLevel(float rms)
    {
        double dbfs = VoiceLevelMeterScale.RmsToDbfs(rms);
        lock (_levelLock)
        {
            _voiced = _detector.Push(rms);
            _smoothedDbfs = VoiceLevelMeterScale.Smooth(_smoothedDbfs, dbfs);
            if (dbfs > _framePeakDbfs) _framePeakDbfs = dbfs;
        }
    }

    // ── Detector and marks ────────────────────────────────────────────────────

    // A detector on the current voice level, its two levels for the marks. The
    // new detector starts silent, like a dictation's; the next frame decides.
    // A cleared field (NaN) keeps the marks where they were.
    private void RebuildDetector()
    {
        double level = _viewModel?.VoiceLevelDbfs ?? CaptureSettingsService.Instance.Current.VoiceLevelDbfs;
        if (double.IsNaN(level)) return;

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
        double? smoothed;
        bool voiced;
        double framePeak;
        lock (_levelLock)
        {
            smoothed = _smoothedDbfs;
            voiced = _voiced;
            framePeak = _framePeakDbfs;
            _framePeakDbfs = double.NegativeInfinity;
        }

        // Nothing yet from the microphone: the meter stays empty.
        if (smoothed is not { } level) return;

        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (!double.IsNegativeInfinity(framePeak))
        {
            (_peakDbfs, _peakHeldSince) = VoiceLevelMeterScale.NextPeak(_peakDbfs, _peakHeldSince, framePeak, now);
            _recent.Enqueue((now, framePeak));
        }
        while (_recent.Count > 0 && now - _recent.Peek().AtSeconds > VoiceLevelMeterScale.LoudestWindowSeconds)
            _recent.Dequeue();

        PaintBar(level, voiced);

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
            _smoothedDbfs = null;
            _voiced = false;
            _framePeakDbfs = double.NegativeInfinity;
            _detector.Reset();
        }
        _peakDbfs = double.NegativeInfinity;
        _peakHeldSince = 0;
        _recent.Clear();

        VoicedFillRect.Width = 0;
        SilentFillRect.Width = 0;
        VoicedFillRect.Visibility = Visibility.Collapsed;
        SilentFillRect.Visibility = Visibility.Visible;
        PeakRect.Visibility = Visibility.Collapsed;

        LevelReadout.Value = null;
        VoiceReadout.Value = null;
        VoiceReadout.Status = ReadoutStatus.None;
        LoudestReadout.Value = null;
    }

    // Rounded to the dB, as integers so a level just under zero never reads -0.
    private static string FormatDbfs(double dbfs)
        => Loc.Format("RecordingVoiceMeter_Dbfs_Format", (int)Math.Round(dbfs));
}
