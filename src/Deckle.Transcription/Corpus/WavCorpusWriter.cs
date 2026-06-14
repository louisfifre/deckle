using System.IO;
using Deckle.Core;

namespace Deckle.Transcription;

// ── WavCorpusWriter ─────────────────────────────────────────────────────────
//
// Pure audio passthrough for the normalized corpus (ADR-0006). Writes the
// 16 kHz mono PCM provided to whisper_full as signed 16-bit WAV, one file per
// transcription, under `<telemetry-root>/audio/<transcription_id>.wav`. Flat:
// no slug, no per-profile subfolder. Audio is universal and deduplicated; ASR
// and rewrite JSONL lines refer to the same WAV through their `audio_file`
// field (basename relative to the `audio/` folder).
//
// int16 quantization (not float32): half the disk use, playback remains
// universal in any WAV viewer, and offline retranscription accepts both. The
// pipeline provides float [-1, 1] (the exact buffer consumed by whisper.cpp),
// clamped on write to defend against a possible out-of-range value.
//
// Returns the relative basename (`<id>.wav`) on success; this is what corpus
// events stamp into `audio_file`. Null on failure, so the emitter surfaces an
// empty string in the payload rather than propagating an exception that would
// break transcription.
//
// Carry-over from wave 6: this helper used to live in `Deckle.Logging` next to
// `CorpusPaths`. Relocated here because its only business consumer is
// `TranscriptionEngine`; `CorpusPaths` stays in `Deckle.Core` because it is
// also consumed by consent dialogs on the `Deckle.Settings` side (which cannot
// depend on `Deckle.Transcription` without introducing a cycle).
public static class WavCorpusWriter
{
    private const int    SampleRate     = 16_000;
    private const short  BitsPerSample  = 16;
    private const short  NumChannels    = 1;
    private const string AudioSubfolder = "audio";

    public static string? Write(string transcriptionId, float[] audio)
    {
        if (audio is null || audio.Length == 0) return null;
        if (string.IsNullOrWhiteSpace(transcriptionId)) return null;

        string root = CorpusPaths.GetDirectoryPath();

        try
        {
            string audioDir = Path.Combine(root, AudioSubfolder);
            Directory.CreateDirectory(audioDir);

            // transcriptionId is a Guid "N" (32 hex without dashes) emitted by
            // TranscriptionEngine; it is already filesystem-safe. No Sanitize
            // here by principle: if the ID format changes one day, the
            // contract must remain a safe identifier.
            string fileName = transcriptionId + ".wav";
            string path = Path.Combine(audioDir, fileName);
            WritePcm16(path, audio);

            // Basename relative to `audio/`: this is what corpus events put in
            // `audio_file` so an offline tool can resolve the WAV by joining
            // `<telemetry>/audio/` + basename.
            return fileName;
        }
        catch
        {
            // Writing must never break transcription.
            return null;
        }
    }

    private static void WritePcm16(string path, float[] audio)
    {
        int byteRate    = SampleRate * NumChannels * (BitsPerSample / 8);
        short blockAlign = (short)(NumChannels * (BitsPerSample / 8));
        int dataBytes   = audio.Length * (BitsPerSample / 8);
        int riffSize    = 36 + dataBytes;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var bw = new BinaryWriter(fs);

        // RIFF header.
        bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        bw.Write(riffSize);
        bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });

        // fmt subchunk — PCM (format code 1).
        bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(NumChannels);
        bw.Write(SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(BitsPerSample);

        // data subchunk.
        bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(dataBytes);

        // float [-1, 1] → int16 with clamp. Recording path already stays
        // in range; the clamp defends against the occasional out-of-band
        // sample that would wrap around on an unchecked cast.
        for (int i = 0; i < audio.Length; i++)
        {
            float s = audio[i];
            if (s >  1f) s =  1f;
            if (s < -1f) s = -1f;
            short v = (short)(s * short.MaxValue);
            bw.Write(v);
        }
    }
}
