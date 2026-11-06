namespace Deckle.Audio;

// ── AudioLevelMapper ──────────────────────────────────────────────────────────
//
// Pure mic-RMS-to-perceptual-level mapping. Lives in Deckle.Audio
// because it's signal processing of microphone data — sister concept to
// MicrophoneCapture (the source). Any UI module that wants to react to live
// mic level (HUD stroke opacity today, Ask-Ollama text glow tomorrow) reads
// `RmsToPerceptualLevel` and consumes the [0, 1] result however it
// wants.
//
// The dBFS window is fixed for now: two constants, no setting, no
// calibration. A single voice level, measured once, will anchor the window
// later. (The sliders and the auto-calibration that used to move it were
// removed; the sliders left the Recording page on 2026-10-14.)
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
    // since per-window state shouldn't be a global.
    public static float EmaAlpha = 0.25f;

    // Linear RMS mapped through the fixed dBFS window [MinDbfs, MaxDbfs],
    // which folds the dBFS range into a linear [0, 1] level.
    public const float MinDbfs = -55f;
    public const float MaxDbfs = -32f;

    // Pure RMS → [0, 1] perceptual level. Linear RMS in (zero or less
    // returns 0 — silence / gate). Caller owns the EMA smoother.
    public static float RmsToPerceptualLevel(float rms)
    {
        if (rms <= 0f) return 0f;
        float dbfs = 20f * MathF.Log10(rms);
        float t = (dbfs - MinDbfs) / (MaxDbfs - MinDbfs);
        return Math.Clamp(t, 0f, 1f);
    }
}
