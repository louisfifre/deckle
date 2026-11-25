using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Settings;

namespace Deckle.Transcription.Views;

// ViewModel for TranscriptionPage. Load() pulls from the settings POCOs, property
// changes push back via PushToSettings(); _isSyncing prevents re-saving during
// Load().
//
// NumberBox-bound properties are double (NumberBox.Value type). The NaN guards
// in the change handlers and PushToSettings prevent saving when the user clears
// a field.
//
// Partial properties (not fields) for WinRT/AOT compatibility (MVVMTK0045).
public partial class TranscriptionViewModel : ObservableObject
{
    private bool _isSyncing;

    public TranscriptionViewModel()
    {
        _isSyncing = true;

        ModelsDirectory = "";
        var engine = new EngineSettings();
        ParakeetModelDirectory = engine.ParakeetModelDirectory;
        ParakeetEncoder = engine.ParakeetEncoder;
        UseGpu = engine.UseGpu;
        VadEnabled = true;
        StreamingEnabled = false;
        MaxRecordingDurationSeconds = new Deckle.Audio.CaptureSettings().MaxRecordingDurationSeconds;

        // Speech-trim seeds read from the POCO initializers (their single
        // source), overwritten by Load().
        var speechTrim = new SpeechTrimSettings();
        VadThreshold = speechTrim.Threshold;
        VadMinSilenceDurationMs = speechTrim.MinSilenceDurationMs;
        VadSpeechPadMs = speechTrim.SpeechPadMs;

        // Segmenter seeds read from the segmenter POCO initializers (their single
        // source), overwritten by Load().
        var segmenter = new EnergySegmenterSettings();
        SegHangoverMaxMs = segmenter.HangoverMaxMs;
        SegHangoverMinMs = segmenter.HangoverMinMs;
        SegHangoverRampStartMs = segmenter.HangoverRampStartMs;
        SegHangoverRampEndMs = segmenter.HangoverRampEndMs;
        SegMarginMs = segmenter.MarginMs;
        SegCurveX1 = segmenter.HangoverCurveX1;
        SegCurveY1 = segmenter.HangoverCurveY1;
        SegCurveX2 = segmenter.HangoverCurveX2;
        SegCurveY2 = segmenter.HangoverCurveY2;
        MinSpeechMs = new StreamingSettings().MinSpeechMs;
        SeedLexicon();

        // Dictation-experience seeds from the shell POCO initializers (the single
        // source the shell's SettingsService persists), overwritten by Load().
        var overlay = new OverlaySettings();
        OverlayEnabled = overlay.Enabled;
        OverlayFadeOnProximity = overlay.FadeOnProximity;
        OverlayPosition = overlay.Position;
        TranscriptPanelSide = NormalizeTranscriptPanelSide(overlay.TranscriptPanelSide);

        // The dataset consents (latency, corpus) live in TranscriptionTelemetryViewModel,
        // composed on the Diagnostics page.

        // _isSyncing stays true until Load(), so no setter saves the seeds.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var s = TranscriptionSettingsService.Instance.Current;
            ModelsDirectory = s.ModelsDirectory;
            ParakeetModelDirectory = s.Engine.ParakeetModelDirectory;
            ParakeetEncoder = s.Engine.ParakeetEncoder;
            UseGpu = s.Engine.UseGpu;
            VadEnabled = s.Streaming.SpeechTrim.Enabled;
            VadThreshold = s.Streaming.SpeechTrim.Threshold;
            VadMinSilenceDurationMs = s.Streaming.SpeechTrim.MinSilenceDurationMs;
            VadSpeechPadMs = s.Streaming.SpeechTrim.SpeechPadMs;

            StreamingEnabled = s.Streaming.Strategy == PipelineStrategyKind.Streaming;
            SegHangoverMaxMs = s.Streaming.Segmenter.HangoverMaxMs;
            SegHangoverMinMs = s.Streaming.Segmenter.HangoverMinMs;
            SegHangoverRampStartMs = s.Streaming.Segmenter.HangoverRampStartMs;
            SegHangoverRampEndMs = s.Streaming.Segmenter.HangoverRampEndMs;
            SegCurveX1 = s.Streaming.Segmenter.HangoverCurveX1;
            SegCurveY1 = s.Streaming.Segmenter.HangoverCurveY1;
            SegCurveX2 = s.Streaming.Segmenter.HangoverCurveX2;
            SegCurveY2 = s.Streaming.Segmenter.HangoverCurveY2;
            SegMarginMs = s.Streaming.Segmenter.MarginMs;
            MinSpeechMs = s.Streaming.MinSpeechMs;
            LoadLexicon(s);

            // The recording cap lives in the capture settings, not here.
            MaxRecordingDurationSeconds = Deckle.Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds;

            // The shell's SettingsService persists these, not TranscriptionSettings.
            var shell = SettingsService.Instance.Current;
            OverlayEnabled = shell.Overlay.Enabled;
            OverlayFadeOnProximity = shell.Overlay.FadeOnProximity;
            OverlayPosition = shell.Overlay.Position;
            TranscriptPanelSide = NormalizeTranscriptPanelSide(shell.Overlay.TranscriptPanelSide);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void PushToSettings()
    {
        var s = TranscriptionSettingsService.Instance.Current;

        s.ModelsDirectory = ModelsDirectory;
        s.Engine.ParakeetModelDirectory = ParakeetModelDirectory;
        s.Engine.ParakeetEncoder = ParakeetEncoder;
        s.Engine.UseGpu = UseGpu;

        s.Streaming.SpeechTrim.Enabled = VadEnabled;
        s.Streaming.SpeechTrim.Threshold = (float)VadThreshold;
        s.Streaming.SpeechTrim.MinSilenceDurationMs = (int)VadMinSilenceDurationMs;
        s.Streaming.SpeechTrim.SpeechPadMs = (int)VadSpeechPadMs;


        s.Streaming.Strategy = StreamingEnabled
            ? PipelineStrategyKind.Streaming
            : PipelineStrategyKind.Monolithic;
        if (!double.IsNaN(SegHangoverMaxMs))       s.Streaming.Segmenter.HangoverMaxMs       = (int)SegHangoverMaxMs;
        if (!double.IsNaN(SegHangoverMinMs))       s.Streaming.Segmenter.HangoverMinMs       = (int)SegHangoverMinMs;
        if (!double.IsNaN(SegHangoverRampStartMs)) s.Streaming.Segmenter.HangoverRampStartMs = (int)SegHangoverRampStartMs;
        if (!double.IsNaN(SegHangoverRampEndMs))   s.Streaming.Segmenter.HangoverRampEndMs   = (int)SegHangoverRampEndMs;
        if (!double.IsNaN(SegCurveX1))             s.Streaming.Segmenter.HangoverCurveX1     = SegCurveX1;
        if (!double.IsNaN(SegCurveY1))             s.Streaming.Segmenter.HangoverCurveY1     = SegCurveY1;
        if (!double.IsNaN(SegCurveX2))             s.Streaming.Segmenter.HangoverCurveX2     = SegCurveX2;
        if (!double.IsNaN(SegCurveY2))             s.Streaming.Segmenter.HangoverCurveY2     = SegCurveY2;
        if (!double.IsNaN(SegMarginMs))           s.Streaming.Segmenter.MarginMs            = (int)SegMarginMs;
        if (!double.IsNaN(MinSpeechMs))            s.Streaming.MinSpeechMs                   = (int)MinSpeechMs;

        PushLexicon(s);

        TranscriptionSettingsService.Instance.Save();
    }
}
