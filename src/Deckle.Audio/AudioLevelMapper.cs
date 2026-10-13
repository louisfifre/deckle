namespace Deckle.Audio;

// ── AudioLevelMapper ──────────────────────────────────────────────────────────
//
// Pure mic-RMS-to-perceptual-level mapping, audio-domain math kept out of the
// HUD. A UI module that reacts to the live mic level reads
// `RmsToPerceptualLevel` and owns its own EMA smoother state.
//
// The public statics are mutable by design, written live through Apply: by the
// Recording settings sliders, and through App.ApplyLevelWindow at boot and by
// engine-side auto-calibration. The Playground reads them through its preview
// and never writes them. The audio thread reads RmsToPerceptualLevel while the
// UI thread writes the statics; single-precision float writes are atomic, so
// no lock is needed. Defaults match LevelWindowSettings.
public static class AudioLevelMapper
{
    // EMA smoothing factor applied AFTER the dBFS remap, per 50 ms sample:
<<<<<<< HEAD
    // next = previous · EmaAlpha + sample · (1 − EmaAlpha), so the time
    // constant is τ = −T / ln(EmaAlpha) ≈ 36 ms, under one sample. It irons
    // the sample grid into a continuous ramp and leaves word-scale intonation
    // intact; the 50 ms key-frame in ProcessingStroke.UpdateLevel does the rest
    // of the visible smoothing. The utterance countdown rises through this same
    // alpha, so the gauge and the outline it replaces settle at one pace.
=======
    // next = previous · EmaAlpha + sample · (1 − EmaAlpha). 0.25 keeps a
    // quarter of the previous value, so the time constant is
    // τ = −T / ln(EmaAlpha) ≈ 0.05 / 1.386 ≈ 36 ms — under one sample.
    // (An earlier comment read 0.15 s; that would need EmaAlpha ≈ 0.72.)
    // It irons the sample grid into a continuous ramp and leaves every
    // word-scale intonation (200–500 ms) intact; the 50 ms key-frame in
    // ProcessingStroke.UpdateLevel does the rest of the visible smoothing.
    // The consumer owns the smoother state (the EMA value to feed back)
    // since per-window state shouldn't be a global.
>>>>>>> main
    public static float EmaAlpha = 0.25f;

    // Linear RMS is folded through the dBFS window [MinDbfs, MaxDbfs] into a
    // linear t in [0, 1], then reshaped by the power curve t^p. MinDbfs is the
    // auto-calibration starting point; the engine retunes it when
    // AutoCalibrationEnabled is on.
    public static float MinDbfs           = -55f;
    public static float MaxDbfs           = -32f;
    public static float DbfsCurveExponent = 1.0f;

    public static float RmsToPerceptualLevel(float rms)
    {
        if (rms <= 0f) return 0f;
        float dbfs = 20f * MathF.Log10(rms);
        float t = (dbfs - MinDbfs) / (MaxDbfs - MinDbfs);
        t = Math.Clamp(t, 0f, 1f);
        // Guarded against p ≤ 0 so a zero exponent in the settings file cannot
        // break the mapping.
        float p = DbfsCurveExponent;
        if (p <= 0f) return t;
        return MathF.Pow(t, p);
    }

    public static void Apply(LevelWindowSettings window)
    {
        if (window is null) return;
        MinDbfs           = window.MinDbfs;
        MaxDbfs           = window.MaxDbfs;
        DbfsCurveExponent = window.DbfsCurveExponent;
    }
}
