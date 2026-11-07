namespace Deckle.Audio;

// How the microphone was opened. Raw is the point of the capture: no effects,
// no gain control, no noise suppression between the device and Deckle, so the
// processing Deckle applies is the only one and the same on every machine.
// Default is the fallback when the device cannot give it.
public enum CaptureMode
{
    // The raw processing mode (WASAPI AUDCLNT_STREAMOPTIONS_RAW): the stream
    // bypasses the driver's signal processing.
    Raw,

    // The mode Windows picks for the device, with whatever effects the driver
    // applies in it.
    Default,
}

// What a capture reports once the microphone is open (IMicrophoneCapture.Opened):
// enough for a surface to say what it is listening to and how.
//   DeviceName       the device's friendly name, as Windows shows it.
//   Mode             the processing mode obtained.
//   Exclusive        Deckle holds the device alone.
//   SampleRate / Channels   the device's own format, before Deckle converts it
//                    to 16 kHz mono.
//   InputLatencyMs   the latency the audio backend reports for the stream.
public readonly record struct CaptureSource(
    string DeviceName,
    CaptureMode Mode,
    bool Exclusive,
    int SampleRate,
    int Channels,
    double InputLatencyMs);
