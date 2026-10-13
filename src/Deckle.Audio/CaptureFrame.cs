namespace Deckle.Audio;

// One 50 ms sub-window of captured audio, emitted live by the capture loop on
// the recording thread (see WaveInLoop.EmitSubWindows). Samples is mono 16 kHz
// float[-1, 1] (800 samples), a fresh allocation per frame — never an alias of
// the unmanaged ring buffer, so it is safe to hand off to a subscriber and keep.
// Rms is the linear RMS [0, 1] of those same samples — the RAW level, identical
// to what the AudioLevel event carries, BEFORE any perceptual mapping
// (AudioLevelMapper).
//
// Use-agnostic: the frame says nothing about why we capture. The Frame event is
// opt-in and pays the float conversion only when something subscribes; the HUD
// uses the lighter AudioLevel event.
public readonly record struct CaptureFrame(
    System.ReadOnlyMemory<float> Samples,
    float Rms);
