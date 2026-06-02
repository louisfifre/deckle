namespace Deckle.Audio;

// One 50 ms sub-window of captured audio, emitted live by the capture loop on
// the recording thread (see WaveInLoop.EmitSubWindows). Samples is mono 16 kHz
// float[-1, 1] (800 samples), a fresh allocation per frame — never an alias of
// the unmanaged ring buffer, so it is safe to hand off to a subscriber and keep.
// Rms is the linear RMS [0, 1] of those same samples — the RAW level, identical
// to what the AudioLevel event carries, BEFORE any perceptual mapping
// (AudioLevelMapper).
//
// Use-agnostic, like the rest of this module: the frame says nothing about why
// we capture. Today the streaming transcription socle subscribes to it to feed
// its energy segmenter; a future consumer (Ask-Ollama) could segment the same
// stream its own way. The HUD keeps using the lighter AudioLevel event — the
// Frame event is opt-in and only pays the float conversion when something
// subscribes.
public readonly record struct CaptureFrame(
    System.ReadOnlyMemory<float> Samples,
    float Rms);
