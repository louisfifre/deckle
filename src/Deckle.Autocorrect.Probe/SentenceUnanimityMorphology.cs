using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Deckle.Autocorrect;

namespace Deckle.Autocorrect.Probe;

internal static class SentenceUnanimityMorphology
{
    public static SentenceUnanimityMorphologyResource Load()
    {
        string path = Path.Combine(
            AutocorrectLexiconArtifacts.DataDirectory,
            AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException(
                "The frozen French verb morphology artifact was not found.",
                path);

        byte[] artifact = File.ReadAllBytes(path);
        string sha256 = Convert.ToHexString(SHA256.HashData(artifact));
        using var bytes = new MemoryStream(artifact, writable: false);
        using var gzip = new GZipStream(bytes, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        VerbMorphology data = VerbMorphology.LoadTsv(reader);
        return new SentenceUnanimityMorphologyResource(
            data,
            new SentenceUnanimityMorphologyReport(
                AutocorrectLexiconArtifacts.VerbMorphologyFrenchFileName,
                sha256,
                artifact.LongLength,
                data.Count,
                data.SkippedLines));
    }
}

internal sealed record SentenceUnanimityMorphologyResource(
    VerbMorphology Data,
    SentenceUnanimityMorphologyReport Report);

internal sealed record SentenceUnanimityMorphologyReport(
    string ArtifactName,
    string Sha256,
    long Bytes,
    int FormCount,
    int SkippedLines);
