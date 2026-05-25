using System.IO;
using Deckle.Core;

namespace Deckle.Transcription.Corpus;

// ── WavCorpusWriter ─────────────────────────────────────────────────────────
//
// Pure passe-plat audio du corpus normalisé (ADR-0011). Écrit le 16 kHz
// mono PCM fourni à whisper_full comme un WAV 16-bit signé, un fichier
// par transcription, sous `<telemetry-root>/audio/<transcription_id>.wav`.
// Plat — pas de slug, pas de sous-dossier par profil. L'audio est
// universel et dédupliqué ; les lignes JSONL ASR et rewrite réfèrent
// au même WAV via leur champ `audio_file` (basename relatif au dossier
// `audio/`).
//
// Quantization int16 (pas float32) : la moitié du disque, la lecture
// reste universelle dans n'importe quel viewer WAV, et la re-
// transcription offline accepte les deux. Le pipeline fournit du
// float [-1, 1] (l'exact buffer que whisper.cpp consomme), clampé à
// l'écriture pour défendre contre une éventuelle valeur hors plage.
//
// Retourne le basename relatif (`<id>.wav`) en succès — c'est ce que
// les events corpus stampent dans `audio_file`. Null en cas d'échec,
// pour que l'émetteur surface une string vide dans le payload plutôt
// que de propager une exception qui casserait la transcription.
//
// Carry-over de la vague 6 : ce helper vivait jadis dans `Deckle.Logging`
// aux côtés de `CorpusPaths`. Relocalisé ici parce que son unique
// consommateur métier est `TranscriptionEngine` ; `CorpusPaths` reste
// dans `Deckle.Core` parce qu'il est aussi consommé par les dialogs
// de consentement côté `Deckle.Settings` (qui ne peut pas dépendre de
// `Deckle.Transcription` sans introduire un cycle).
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

            // transcriptionId est un Guid "N" (32 hex sans tirets) émis
            // par TranscriptionEngine — c'est déjà filesystem-safe. Pas
            // de Sanitize ici par principe : si un jour l'ID change de
            // format, le contrat doit rester un identifiant sûr.
            string fileName = transcriptionId + ".wav";
            string path = Path.Combine(audioDir, fileName);
            WritePcm16(path, audio);

            // Basename relatif à `audio/` — c'est ce que les events
            // corpus mettent dans `audio_file` pour qu'un outil offline
            // résolve le WAV en joignant `<telemetry>/audio/` + basename.
            return fileName;
        }
        catch
        {
            // L'écriture ne doit jamais casser la transcription.
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
