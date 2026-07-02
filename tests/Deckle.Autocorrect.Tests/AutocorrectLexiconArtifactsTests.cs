using System.IO.Compression;
using System.Text;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The live engine must never treat the historical full English list as the
// protected-literal tier. Only the restricted globish seed artifact activates
// the global-English guard.
[Trait("Category", "unit")]
public sealed class AutocorrectLexiconArtifactsTests : IDisposable
{
    private const string LegacyFullEnglishFileName = "lexicon-en.tsv.gz";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"deckle-lexicons-{Guid.NewGuid():N}");

    public AutocorrectLexiconArtifactsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void GlobalEnglishSeedIsAbsentWhenOnlyTheLegacyFullListExists()
    {
        WriteGzipTsv(LegacyFullEnglishFileName, "the\t60000\n");

        Assert.Null(AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(_dir));
    }

    [Fact]
    public void GlobalEnglishSeedLoadsOnlyTheRestrictedSeedArtifact()
    {
        WriteGzipTsv(LegacyFullEnglishFileName, "the\t60000\n");
        WriteGzipTsv(AutocorrectLexiconArtifacts.GlobalEnglishSeedFileName, "mode\t500\n");

        var seed = AutocorrectLexiconArtifacts.LoadGlobalEnglishSeed(_dir);

        Assert.NotNull(seed);
        Assert.True(seed!.Contains("mode"));
        Assert.Equal(500, seed.FrequencyOf("mode"));
        Assert.False(seed.Contains("the"));
    }

    private void WriteGzipTsv(string fileName, string content)
    {
        string path = Path.Combine(_dir, fileName);
        using var file = File.Create(path);
        using var gz = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gz, new UTF8Encoding(false));
        writer.Write(content);
    }
}
