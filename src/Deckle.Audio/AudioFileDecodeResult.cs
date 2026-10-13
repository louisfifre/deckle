namespace Deckle.Audio;

// Categorical outcome of AudioFileDecoder.Decode. No localized strings here:
// the transcription engine maps each status to its user-visible text.
//   Decoded           — success. Pcm holds 16 kHz mono float[-1, 1].
//   FileNotFound      — the path does not exist (checked before any MF call).
//   UnsupportedFormat — no handler for the container, or no decoder for the
//                       codec.
//   NoAudioTrack      — the file opened but exposes no audio stream.
//   ProtectedContent  — DRM-protected content (the Source Reader does not
//                       support DRM by design).
//   ReadError         — anything else: corrupt file, mid-stream read failure,
//                       platform init failure.
public enum AudioFileDecodeStatus
{
    Decoded,
    FileNotFound,
    UnsupportedFormat,
    NoAudioTrack,
    ProtectedContent,
    ReadError,
}

// Returned from AudioFileDecoder.Decode. Pcm is float[-1, 1] mono 16 kHz, the
// format MicrophoneCapture produces. On any failure Pcm is empty and
// DurationSeconds is 0.
public readonly record struct AudioFileDecodeResult(
    AudioFileDecodeStatus Status,
    ReadOnlyMemory<float> Pcm,
    double DurationSeconds);
