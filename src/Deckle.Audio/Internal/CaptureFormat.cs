namespace Deckle.Audio.Internal;

// The device's own format, as the stream delivers it: interleaved 32-bit float
// at the device's sample rate, one sample per channel per frame. The capture
// always asks the backend for float32 at the rate and channel count the device
// mixes in, so Windows converts nothing; the conversion to 16 kHz mono float is
// Deckle's own (MonoConverter, then StreamingResampler).
internal readonly record struct CaptureFormat(int SampleRate, int Channels);
