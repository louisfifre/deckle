namespace Deckle.Audio;

// ── AudioLevelMapper ──────────────────────────────────────────────────────────
//
// Pure mic-RMS-to-perceptual-level mapping, audio-domain math kept out of the
// HUD. A UI module that reacts to the live mic level reads
// `RmsToPerceptualLevel` and owns its own EMA smoother state.
//
// The public statics are written live through Apply: by the Recording settings
// sliders, and through App.ApplyLevelWindow at boot and by engine-side
// auto-calibration. The Playground reads them through its preview and never
// writes them. The audio thread reads RmsToPerceptualLevel while the UI thread
// writes the statics; single-precision float writes are atomic, so no lock is
// needed. Defaults match LevelWindowSettings.
public static class AudioLevelMapper
{
    // EMA smoothing factor applied AFTER the dBFS remap. EmaAlpha 0.25
    // at 20 Hz source → τ = -T / ln(alpha) ≈ 0.05 / 0.328 ≈ 0.15 s —
    // fast enough to track intonations at the word scale (typical word
    // = 200–500 ms) while still ironing out the sample grid into a
    // continuous ramp.
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
        // Power-curve response. p = 1 is linear; p > 1 compresses the
        // low end and expands the high end. Guarded against p ≤ 0 so a
        // zero exponent in the settings file cannot nuke the mapping.
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
