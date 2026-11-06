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
// The dBFS window is anchored on the user's voice level
// (CaptureSettings.VoiceLevelDbfs), the same level the segmenter cuts on: the
// outline is dark at the voice detector's exit level and full 20 dB above it.
// The app calls Apply at startup and whenever the capture settings change;
// until then the window sits on the shipped default level.
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

    // Width of the window above the voice level, in dB.
    public const float WindowSpanDb = 20f;

    // The curve applied to the position in the window. The map stays
    // continuous: the HUD must not look like a gate, an outline that switched
    // on at once would flicker. The square root lifts the bottom of the window
    // so that a frame a few dB above the level already reads clearly lit,
    // sqrt(3/20) ≈ 0.39 at 3 dB rather than 0.15 on a straight line: a soft
    // voice that opens an utterance does not leave the ring almost dark.
    // Interpretation of the research note's P6, to be checked against the eye.
    public const float Exponent = 0.5f;

    // The window, swapped whole so a reader never sees a floor from one level
    // and a ceiling from another. Written at startup and on each settings
    // change (from whichever thread raises it), read on the audio thread at
    // every sample; Volatile publishes the new window to that thread.
    private sealed record Window(float MinDbfs, float MaxDbfs);

    private static Window _window = null!;

    // The defaults' own level, so the HUD is anchored before the app applies
    // the stored one.
    static AudioLevelMapper() => Apply(new CaptureSettings().VoiceLevelDbfs);

    // Where the outline is dark: the voice detector's exit level.
    public static float MinDbfs => Volatile.Read(ref _window).MinDbfs;

    // Where the outline is full: WindowSpanDb above the voice level.
    public static float MaxDbfs => Volatile.Read(ref _window).MaxDbfs;

    // Anchors the window on a voice level, in dBFS.
    public static void Apply(double voiceLevelDbfs)
    {
        float min = (float)voiceLevelDbfs;
        Volatile.Write(ref _window, new Window(min, min + WindowSpanDb));
    }

    // Pure RMS → [0, 1] perceptual level. Linear RMS in (zero or less
    // returns 0 — silence / gate). Caller owns the EMA smoother.
    public static float RmsToPerceptualLevel(float rms)
    {
        if (rms <= 0f) return 0f;
        Window w = Volatile.Read(ref _window);
        float dbfs = 20f * MathF.Log10(rms);
        float t = Math.Clamp((dbfs - w.MinDbfs) / (w.MaxDbfs - w.MinDbfs), 0f, 1f);
        return MathF.Pow(t, Exponent);
    }
}
