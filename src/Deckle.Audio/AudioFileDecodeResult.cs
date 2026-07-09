namespace Deckle.Audio;

// Categorical outcome of AudioFileDecoder.Decode — closed enum, no localized
// strings here. Capture-side layers stay free of any UI vocabulary (mirrors
// CaptureOutcome / MicErrorKind); the transcription engine maps each status to a
// user-visible feedback via Loc.Get on its side (FileTranscription_DecodeFailed_
// Body_*).
//   Decoded           — success. Pcm holds 16 kHz mono float[-1, 1].
//   FileNotFound      — the path does not exist (checked before any MF call).
//   UnsupportedFormat — no byte-stream handler / scheme handler for the
//                       container (MF_E_UNSUPPORTED_BYTESTREAM_TYPE /
//                       MF_E_UNSUPPORTED_SCHEME at reader creation), or no
//                       decoder for the codec (MF_E_TOPO_CODEC_NOT_FOUND /
//                       MF_E_INVALIDMEDIATYPE at format negotiation).
//   NoAudioTrack      — the file opened but exposes no audio stream
//                       (MF_E_INVALIDSTREAMNUMBER on the first audio stream).
//   ProtectedContent  — DRM / protected content the source reader refuses (the
//                       Source Reader does not support DRM by design).
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

// Returned from AudioFileDecoder.Decode. Pcm is float[-1, 1] mono 16 kHz — the
// fixed transcription format, identical to what MicrophoneCapture produces — and
// is non-null only when Status == Decoded. DurationSeconds is the decoded audio
// length (decoded sample count / 16000), 0 on any failure.
public readonly record struct AudioFileDecodeResult(
    AudioFileDecodeStatus Status,
    float[]? Pcm,
    double DurationSeconds);
