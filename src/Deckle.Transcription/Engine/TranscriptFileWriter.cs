namespace Deckle.Transcription;

// ── TranscriptFileWriter ──────────────────────────────────────────────────────
//
// Writes a file-transcription result to a .txt on disk, named after the source
// audio file, in the source audio's own directory. A collision resolves with the
// Windows Explorer duplicate convention ("name (2).txt", "name (3).txt", …) so a
// re-run never overwrites an earlier transcript. IOExceptions surface to the
// caller — the engine catches and maps them to a WriteFailed feedback; nothing is
// swallowed here.
internal static class TranscriptFileWriter
{
    // Writes text as UTF-8 without BOM (File.WriteAllText's default) and returns
    // the full path written. The source directory already exists by definition;
    // failure to write there surfaces to the engine instead of silently moving
    // the transcript away from its audio.
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

    // Pure name resolution, factored out so collision handling is unit-testable
    // without touching disk (exists is File.Exists in production). Returns
    // "<baseName>.txt" when free, otherwise the first "<baseName> (n).txt" (n ≥ 2)
    // that exists() rejects — the Windows Explorer duplicate convention. A base
    // name that already ends in " (n)" is not re-parsed: its plain ".txt" is tried
    // first like any other.
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
