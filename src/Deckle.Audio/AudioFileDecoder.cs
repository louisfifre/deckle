using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Deckle.Audio.Internal;

namespace Deckle.Audio;

// Decodes an arbitrary audio file to the fixed transcription format — 16 kHz
// mono float PCM [-1, 1], the exact signal MicrophoneCapture already produces —
// so a file and a live dictation feed the same monolithic pipeline downstream.
//
// The route is Media Foundation's synchronous IMFSourceReader. We force a fully
// specified output type (Float / 32-bit / 16 kHz / mono); since Windows 8 the
// source reader auto-inserts the decoder, the resampler, and the channel
// down-mix to satisfy it, so any container/codec Windows can play — mp3, m4a,
// wav, flac, ogg, wma, … — reaches us already conditioned. We add no DSP here.
//
// Decode is stateless and thread-agnostic, but must run OFF the UI thread: it
// initializes COM as MTA and blocks draining the whole file. A bad file is a
// status, never an exception — only a caller programming error (an STA thread)
// throws.
public sealed class AudioFileDecoder
{
    // The fixed transcription format. Kept in lock-step with MicrophoneCapture's
    // capture format (16 kHz mono) so both producers hand the pipeline the same
    // shape; float/32-bit is Media Foundation's uncompressed representation,
    // converted to [-1, 1] amplitude with no further scaling.
    private const int TargetSampleRate    = 16000;
    private const uint TargetChannels     = 1;
    private const uint TargetBitsPerSample = 32;

    /// <summary>
    /// Decodes the audio file at <paramref name="path"/> to 16 kHz mono float
    /// PCM. Synchronous and blocking — decode the whole file before returning.
    /// </summary>
    /// <param name="path">Full path to a local audio file.</param>
    /// <returns>
    /// An <see cref="AudioFileDecodeResult"/> whose <see cref="AudioFileDecodeResult.Status"/>
    /// tells the caller what happened: <see cref="AudioFileDecodeStatus.Decoded"/>
    /// carries the PCM in <see cref="AudioFileDecodeResult.Pcm"/> (non-null only
    /// then); every other value carries a null buffer and names why the file
    /// could not be decoded. The method never throws for a bad file.
    /// </returns>
    /// <remarks>
    /// Must be called off the UI thread. Media Foundation requires an MTA
    /// apartment on the calling thread; an STA thread is a programming error and
    /// surfaces as <see cref="InvalidOperationException"/>, not a status.
    /// </remarks>
    public static AudioFileDecodeResult Decode(string path)
    {
        // A missing path is a status, decided before any Media Foundation work —
        // MFCreateSourceReaderFromURL would otherwise turn it into an opaque
        // resolver error.
        if (!File.Exists(path))
        {
            LogFailure(AudioFileDecodeStatus.FileNotFound, MediaFoundationInterop.S_OK);
            return new AudioFileDecodeResult(AudioFileDecodeStatus.FileNotFound, null, 0);
        }

        var sw = Stopwatch.StartNew();

        // COM apartment. The contract guarantees Decode runs off the UI thread,
        // so RPC_E_CHANGED_MODE (the thread is already STA) is a caller mistake,
        // surfaced as an exception rather than silently mis-decoding. S_OK and
        // S_FALSE both count as a successful init and are balanced 1:1 with
        // CoUninitialize.
        int coHr = MediaFoundationInterop.CoInitializeEx(0, MediaFoundationInterop.COINIT_MULTITHREADED);
        if (coHr == MediaFoundationInterop.RPC_E_CHANGED_MODE)
        {
            throw new System.InvalidOperationException(
                "AudioFileDecoder.Decode must run on an MTA thread; the calling thread is STA.");
        }
        bool coInitialized = coHr >= 0;

        try
        {
            // MFStartup … MFShutdown balanced 1:1 per decode. A startup failure is
            // environmental (Media Foundation missing / safe mode), mapped to a
            // read error.
            int mfHr = MediaFoundationInterop.MFStartup(
                MediaFoundationInterop.MF_VERSION, MediaFoundationInterop.MFSTARTUP_FULL);
            if (mfHr < 0)
                return Fail(AudioFileDecodeStatus.ReadError, mfHr, sw);

            try
            {
                return DecodeCore(path, sw);
            }
            finally
            {
                MediaFoundationInterop.MFShutdown();
            }
        }
        finally
        {
            if (coInitialized) MediaFoundationInterop.CoUninitialize();
        }
    }

    // Reader creation → stream selection → output-type negotiation → drain. Every
    // COM pointer acquired here is released in this method's finally; helpers
    // release their own transient pointers.
    private static AudioFileDecodeResult DecodeCore(string path, Stopwatch sw)
    {
        nint reader = 0;
        try
        {
            int hr = MediaFoundationInterop.MFCreateSourceReaderFromURL(path, 0, out reader);
            if (hr < 0)
                return Fail(MapReaderCreationError(hr), hr, sw);

            // Deselect everything, then select only the first audio stream — the
            // source reader then never holds onto video frames we would drop.
            MediaFoundationInterop.SourceReaderSetStreamSelection(
                reader, MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            hr = MediaFoundationInterop.SourceReaderSetStreamSelection(
                reader, MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, true);
            if (hr < 0)
            {
                var status = hr == MediaFoundationInterop.MF_E_INVALIDSTREAMNUMBER
                    ? AudioFileDecodeStatus.NoAudioTrack
                    : MapGenericError(hr);
                return Fail(status, hr, sw);
            }

            hr = ConfigureOutputType(reader);
            if (hr < 0)
                return Fail(MapSetTypeError(hr), hr, sw);

            (int readHr, float[] pcm) = ReadAllSamples(reader);
            if (readHr < 0)
                return Fail(MapGenericError(readHr), readHr, sw);

            sw.Stop();
            double durationSeconds = pcm.Length / (double)TargetSampleRate;
            DeckleAudioSource.Log.AudioFileDecoded();
            DeckleAudioSource.Log.AudioFileDecodedDetail(
                path, durationSeconds, pcm.Length, sw.ElapsedMilliseconds);
            return new AudioFileDecodeResult(AudioFileDecodeStatus.Decoded, pcm, durationSeconds);
        }
        finally
        {
            if (reader != 0) Marshal.Release(reader);
        }
    }

    // Builds the minimal partial output type (major/subtype + rate/channels/bits)
    // and sets it on the first audio stream. The source reader fills in the rest
    // and loads whatever decoder + resampler + down-mix the native format needs.
    // GetCurrentMediaType is read back per the source-reader contract to confirm
    // the negotiated type resolved; we forced a fully specified format, so the
    // readback is a validation step and is released immediately.
    private static int ConfigureOutputType(nint reader)
    {
        int hr = MediaFoundationInterop.MFCreateMediaType(out nint mediaType);
        if (hr < 0) return hr;

        try
        {
            hr = MediaFoundationInterop.MediaTypeSetGuid(
                mediaType, MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Audio);
            if (hr < 0) return hr;
            hr = MediaFoundationInterop.MediaTypeSetGuid(
                mediaType, MediaFoundationInterop.MF_MT_SUBTYPE, MediaFoundationInterop.MFAudioFormat_Float);
            if (hr < 0) return hr;
            hr = MediaFoundationInterop.MediaTypeSetUInt32(
                mediaType, MediaFoundationInterop.MF_MT_AUDIO_BITS_PER_SAMPLE, TargetBitsPerSample);
            if (hr < 0) return hr;
            hr = MediaFoundationInterop.MediaTypeSetUInt32(
                mediaType, MediaFoundationInterop.MF_MT_AUDIO_SAMPLES_PER_SECOND, TargetSampleRate);
            if (hr < 0) return hr;
            hr = MediaFoundationInterop.MediaTypeSetUInt32(
                mediaType, MediaFoundationInterop.MF_MT_AUDIO_NUM_CHANNELS, TargetChannels);
            if (hr < 0) return hr;

            hr = MediaFoundationInterop.SourceReaderSetCurrentMediaType(
                reader, MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, mediaType);
            if (hr < 0) return hr;

            int getHr = MediaFoundationInterop.SourceReaderGetCurrentMediaType(
                reader, MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out nint actualType);
            if (getHr >= 0 && actualType != 0) Marshal.Release(actualType);

            return hr; // S_OK
        }
        finally
        {
            Marshal.Release(mediaType);
        }
    }

    // Drains the first audio stream sample-by-sample into one contiguous float
    // buffer. Returns the accumulated PCM on a clean end-of-stream, or a failing
    // HRESULT (empty buffer) on any read error — after which no further reader
    // call is made.
    private static (int hr, float[] pcm) ReadAllSamples(nint reader)
    {
        // ~8 s of headroom before the first doubling; the List grows past that on
        // its own for longer files.
        var samples = new List<float>(capacity: TargetSampleRate * 8);
        float[] scratch = System.Array.Empty<float>();

        while (true)
        {
            int hr = MediaFoundationInterop.SourceReaderReadSample(
                reader, MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM,
                out uint flags, out nint sample);

            if (hr < 0)
            {
                if (sample != 0) Marshal.Release(sample);
                return (hr, System.Array.Empty<float>());
            }

            // A hard stream error: stop at once and make no further reader call.
            if ((flags & MediaFoundationInterop.MF_SOURCE_READERF_ERROR) != 0)
            {
                if (sample != 0) Marshal.Release(sample);
                return (MediaFoundationInterop.E_FAIL, System.Array.Empty<float>());
            }

            if ((flags & MediaFoundationInterop.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                if (sample != 0) Marshal.Release(sample);
                break;
            }

            // A mid-stream format change: re-read the current type to stay in sync,
            // then keep draining. Our forced type should not actually change.
            if ((flags & MediaFoundationInterop.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0)
            {
                int getHr = MediaFoundationInterop.SourceReaderGetCurrentMediaType(
                    reader, MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out nint changedType);
                if (getHr >= 0 && changedType != 0) Marshal.Release(changedType);
            }

            // S_OK with a null sample (e.g. MF_SOURCE_READERF_STREAMTICK): nothing
            // to copy this iteration.
            if (sample == 0)
                continue;

            try
            {
                int convHr = MediaFoundationInterop.SampleConvertToContiguousBuffer(sample, out nint buffer);
                if (convHr < 0)
                    return (convHr, System.Array.Empty<float>());

                try
                {
                    int lockHr = MediaFoundationInterop.BufferLock(buffer, out nint data, out uint byteLength);
                    if (lockHr < 0)
                        return (lockHr, System.Array.Empty<float>());

                    try
                    {
                        int floatCount = (int)(byteLength / sizeof(float));
                        if (floatCount > 0)
                        {
                            if (scratch.Length < floatCount)
                                scratch = new float[floatCount];
                            Marshal.Copy(data, scratch, 0, floatCount);
                            samples.AddRange(new System.ReadOnlySpan<float>(scratch, 0, floatCount));
                        }
                    }
                    finally
                    {
                        MediaFoundationInterop.BufferUnlock(buffer);
                    }
                }
                finally
                {
                    Marshal.Release(buffer);
                }
            }
            finally
            {
                Marshal.Release(sample);
            }
        }

        return (MediaFoundationInterop.S_OK, samples.ToArray());
    }

    // ── HRESULT → status mapping ─────────────────────────────────────────────

    // Reader creation: an unrecognized container/scheme is UnsupportedFormat;
    // otherwise fall through to the DRM / generic split.
    private static AudioFileDecodeStatus MapReaderCreationError(int hr) => hr switch
    {
        MediaFoundationInterop.MF_E_UNSUPPORTED_BYTESTREAM_TYPE => AudioFileDecodeStatus.UnsupportedFormat,
        MediaFoundationInterop.MF_E_UNSUPPORTED_SCHEME          => AudioFileDecodeStatus.UnsupportedFormat,
        _                                                       => MapGenericError(hr),
    };

    // Output-type negotiation: no decoder or a rejected type is UnsupportedFormat;
    // an invalid stream index means the audio stream vanished (NoAudioTrack).
    private static AudioFileDecodeStatus MapSetTypeError(int hr) => hr switch
    {
        MediaFoundationInterop.MF_E_TOPO_CODEC_NOT_FOUND => AudioFileDecodeStatus.UnsupportedFormat,
        MediaFoundationInterop.MF_E_INVALIDMEDIATYPE     => AudioFileDecodeStatus.UnsupportedFormat,
        MediaFoundationInterop.MF_E_INVALIDSTREAMNUMBER  => AudioFileDecodeStatus.NoAudioTrack,
        _                                                => MapGenericError(hr),
    };

    // The catch-all: protected content when the HRESULT falls in the Windows
    // Media DRM band, a read error otherwise.
    private static AudioFileDecodeStatus MapGenericError(int hr) =>
        IsProtectedContent(hr) ? AudioFileDecodeStatus.ProtectedContent : AudioFileDecodeStatus.ReadError;

    // The Source Reader does not support DRM by design; on protected content it
    // surfaces one of the Windows Media DRM HRESULTs. NS_E_NOT_LICENSED plus the
    // NS_E_DRM_* band (0xC00D2700–0xC00D28FF) are the documented codes.
    private static bool IsProtectedContent(int hr)
    {
        uint code = unchecked((uint)hr);
        return code == 0xC00D00CDu                            // NS_E_NOT_LICENSED
            || (code >= 0xC00D2700u && code <= 0xC00D28FFu);  // NS_E_DRM_* band
    }

    private static AudioFileDecodeResult Fail(AudioFileDecodeStatus status, int hr, Stopwatch sw)
    {
        sw.Stop();
        LogFailure(status, hr);
        return new AudioFileDecodeResult(status, null, 0);
    }

    private static void LogFailure(AudioFileDecodeStatus status, int hr)
    {
        DeckleAudioSource.Log.AudioFileDecodeFailed();
        DeckleAudioSource.Log.AudioFileDecodeFailedDetail(status.ToString(), hr);
    }
}
