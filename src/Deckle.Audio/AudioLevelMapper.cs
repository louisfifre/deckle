namespace Deckle.Audio;

// ── AudioLevelMapper ──────────────────────────────────────────────────────────
//
// Pure mic-RMS-to-perceptual-level mapping. Lives in Deckle.Audio
// because it's signal processing of microphone data — sister concept to
// MicrophoneCapture (the source) and CaptureSettings.LevelWindow (the
// calibration). Any UI module that wants to react to live mic level
// (HUD stroke opacity today, Ask-Ollama text glow tomorrow) reads
// `RmsToPerceptualLevel` and consumes the [0, 1] result however it
// wants.
//
// The public statics are written live by the Recording settings sliders,
// by the App at boot and by engine-side auto-calibration; the Playground
// reads them through its preview and never writes them. Atomic float
// reads: the audio thread reads RmsToPerceptualLevel concurrently with
// the UI thread writing the statics. Single-precision float writes are
// atomic on all .NET platforms; no lock needed.
//
// Auto-calibration (engine-side) and manual sliders (Settings ▸ General
// ▸ Recording) write through `App.ApplyLevelWindow` which forwards into
// these statics. Defaults match the shipping calibration documented in
// CaptureSettings.LevelWindowSettings.
//
// Extracted from Controls/HudChrono.xaml.cs on 2026-05-02 — was on the
// HUD control by historical accident; the math is purely audio-domain.
public static class AudioLevelMapper
{
    // EMA smoothing factor applied AFTER the dBFS remap, per 50 ms sample:
    // next = previous · EmaAlpha + sample · (1 − EmaAlpha). 0.25 keeps a
    // quarter of the previous value, so the time constant is
    // τ = −T / ln(EmaAlpha) ≈ 0.05 / 1.386 ≈ 36 ms — under one sample.
    // (An earlier comment read 0.15 s; that would need EmaAlpha ≈ 0.72.)
    // It irons the sample grid into a continuous ramp and leaves every
    // word-scale intonation (200–500 ms) intact; the 50 ms key-frame in
    // ProcessingStroke.UpdateLevel does the rest of the visible smoothing.
    // The consumer owns the smoother state (the EMA value to feed back)
    // since per-window state shouldn't be a global. The utterance countdown
    // (UtteranceCountdownState) rises through this same alpha, so the gauge
    // and the outline it replaces settle at one pace.
    public static float EmaAlpha = 0.25f;

    // Linear RMS mapped through a dBFS window, then through a power
    // curve. The window [MinDbfs, MaxDbfs] folds the dBFS range into a
    // linear [0, 1] parameter t; the power curve t^p then reshapes the
    // response so the visual reacts softly in the lower half and
    // aggressively in the upper half of the window.
    //
    // MinDbfs −55 is the auto-calibration starting point; the engine
    // retunes it session-by-session if AutoCalibrationEnabled is on.
    //
    // DbfsCurveExponent 1.0 restores the old linear mapping; values
    // above 1 push the response to the upper end of the window; below
    // 1 pushes it to the low end (only useful for debugging).
    public static float MinDbfs           = -55f;
    public static float MaxDbfs           = -32f;
    public static float DbfsCurveExponent = 1.0f;

    // Pure RMS → [0, 1] perceptual level. Linear RMS in (zero or less
    // returns 0 — silence / gate). Caller owns the EMA smoother.
    public static float RmsToPerceptualLevel(float rms)
    {
        if (rms <= 0f) return 0f;
        float dbfs = 20f * MathF.Log10(rms);
        float t = (dbfs - MinDbfs) / (MaxDbfs - MinDbfs);
        t = Math.Clamp(t, 0f, 1f);
        // Power-curve response. p = 1 is linear; p > 1 compresses the
        // low end and expands the high end. Guarded against p ≤ 0 so a
        // zero exponent in the settings file cannot nuke the mapping.
        float p = DbfsCurveExponent;
        if (p <= 0f) return t;
        return MathF.Pow(t, p);
    }

    // Push a level-window calibration into the mapper statics. The window's three
    // fields are the mapper's only inputs. Called live by the Recording settings
    // sliders (same assembly, no shell hop), by the App at boot, and by engine-side
    // auto-calibration. Null-safe so an unset window is a no-op.
    public static void Apply(LevelWindowSettings window)
    {
        if (window is null) return;
        MinDbfs           = window.MinDbfs;
        MaxDbfs           = window.MaxDbfs;
        DbfsCurveExponent = window.DbfsCurveExponent;
    }
}
