using System.IO;
using Deckle.Core;

namespace Deckle.Transcription;

// ── WavCorpusWriter ─────────────────────────────────────────────────────────
//
// Pure audio passthrough for the corpus. Writes the take's 16 kHz mono float
// samples (the backend buffer or the raw capture, per the corpus content
// setting) as signed 16-bit WAV, one file per transcription, under
// `<telemetry-root>/audio/<transcription_id>.wav`. Flat: no slug, no
// per-profile subfolder. ASR and rewrite JSONL lines refer to the same WAV
// through their `audio_file` field (basename relative to the `audio/` folder).
//
// int16 quantization (not float32): half the disk use, playback remains
// universal in any WAV viewer, and offline retranscription accepts both.
//
// Returns the relative basename (`<id>.wav`) on success. Null on failure, so
// the emitter surfaces an empty string in the payload rather than propagating
// an exception that would break transcription.
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
            // here by principle: if the ID format changes, the contract must
            // remain a safe identifier.
            string fileName = transcriptionId + ".wav";
            string path = Path.Combine(audioDir, fileName);
            WritePcm16(path, audio);

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

        bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(dataBytes);

        // The clamp defends against an out-of-band sample that would wrap
        // around on the unchecked cast to int16.
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
