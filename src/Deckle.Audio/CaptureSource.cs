namespace Deckle.Audio;

// How the microphone was opened: the processing mode Windows granted, the
// device's own format and its packet period. Raw is the point of the WASAPI
// capture (no effects, no gain control, no noise suppression between the
// device and Deckle); Default is the fallback when the endpoint cannot give it.
public enum CaptureMode
{
    // AUDCLNT_STREAMOPTIONS_RAW: the stream bypasses the audio engine's signal
    // processing modes.
    Raw,

    // The mode Windows picks for the endpoint, with whatever effects the
    // driver applies in it.
    Default,
}

// What a capture reports once the microphone is open (IMicrophoneCapture.Opened):
// enough for a surface to say what it is listening to and how.
//   DeviceName   the endpoint's friendly name, as Windows shows it.
//   Mode         the processing mode obtained.
//   SampleRate / Channels   the device's mix format, before Deckle converts it
//                to 16 kHz mono.
//   PeriodMs     how often the device hands over a packet.
public readonly record struct CaptureSource(
    string DeviceName,
    CaptureMode Mode,
    int SampleRate,
    int Channels,
    double PeriodMs);
