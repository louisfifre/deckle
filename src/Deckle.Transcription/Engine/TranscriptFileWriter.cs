namespace Deckle.Transcription;

// ── TranscriptFileWriter ──────────────────────────────────────────────────────
//
// Writes a file-transcription result beside its source audio. A collision takes
// the Windows Explorer duplicate name ("name (2).txt", …) so a re-run never
// overwrites an earlier transcript. IOExceptions surface to the engine, which
// maps them to the WriteFailed feedback; nothing is swallowed here.
internal static class TranscriptFileWriter
{
    // UTF-8 without BOM (File.WriteAllText's default). A failed write surfaces
    // to the engine instead of silently moving the transcript away from its
    // audio.
    public static string Write(string text, string audioFilePath)
    {
        string sourcePath = Path.GetFullPath(audioFilePath);
        string outputDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new ArgumentException("Audio file path has no parent directory.", nameof(audioFilePath));
        string baseName = Path.GetFileNameWithoutExtension(audioFilePath);
        string target = ResolveTargetPath(baseName, outputDirectory, File.Exists);

        File.WriteAllText(target, text);
        return target;
    }

    // A base name that already ends in " (n)" is not re-parsed: its plain ".txt"
    // is tried first like any other.
    internal static string ResolveTargetPath(
        string baseName, string outputDirectory, Func<string, bool> exists)
    {
        string candidate = Path.Combine(outputDirectory, baseName + ".txt");
        if (!exists(candidate))
            return candidate;

        for (int n = 2; ; n++)
        {
            candidate = Path.Combine(outputDirectory, $"{baseName} ({n}).txt");
            if (!exists(candidate))
                return candidate;
        }
    }
}
