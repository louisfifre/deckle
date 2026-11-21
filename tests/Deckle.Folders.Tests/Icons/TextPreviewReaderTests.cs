using System.Text;
using Xunit;

namespace Deckle.Folders.Tests;

// The beginning of a text file, as the gallery's tile shows it: the usual
// encodings decoded, a character the byte limit cut dropped, at most
// MaxLines lines, nothing for bytes that are not text.
[Trait("Category", "unit")]
public sealed class TextPreviewReaderTests
{
    [Fact]
    public void MarkdownStaysPlainTextLineByLine()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# Titre\r\n\r\n- une liste\r\n\tavec **gras**  \n");

        Assert.Equal("# Titre\n\n- une liste\n avec **gras**", TextPreviewReader.Decode(bytes, truncated: false));
    }

    [Theory]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16le")]
    [InlineData("utf-16be")]
    [InlineData("utf-8")]
    [InlineData("windows-1252")]
    public void FrenchTextReadsTheSameInEachUsualEncoding(string encoding)
    {
        const string text = "Été à la forêt, déjà noël.";
        byte[] bytes = encoding switch
        {
            "utf-8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf-16le" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            "utf-16be" => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
            "utf-8" => Encoding.UTF8.GetBytes(text),
            _ => CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(text),
        };

        Assert.Equal(text, TextPreviewReader.Decode(bytes, truncated: false));
    }

    [Fact]
    public void AUtf8CharacterCutByTheLimitIsDroppedNotReplaced()
    {
        byte[] whole = Encoding.UTF8.GetBytes("abc€");
        byte[] cut = whole[..^1];

        Assert.Equal("abc", TextPreviewReader.Decode(cut, truncated: true));
    }

    [Fact]
    public void AUtf16PairCutByTheLimitIsDroppedNotReplaced()
    {
        byte[] whole = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("ab😀")];
        byte[] cut = whole[..^3];

        Assert.Equal("ab", TextPreviewReader.Decode(cut, truncated: true));
    }

    [Fact]
    public void AtMostMaxLinesAreKeptAfterTheLeadingBlankOnes()
    {
        string text = "\n\n" + string.Join('\n', Enumerable.Range(1, 40).Select(i => $"ligne {i}"));

        string preview = TextPreviewReader.Decode(Encoding.UTF8.GetBytes(text), truncated: false)!;

        string[] lines = preview.Split('\n');
        Assert.Equal(TextPreviewReader.MaxLines, lines.Length);
        Assert.Equal("ligne 1", lines[0]);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x20, 0x0A, 0x09, 0x0D })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 })]
    public void EmptyBlankOrBinaryBytesShowNoPreview(byte[] bytes)
    {
        Assert.Null(TextPreviewReader.Decode(bytes, truncated: false));
    }

    [Theory]
    [InlineData("note.md", true)]
    [InlineData("NOTE.MARKDOWN", true)]
    [InlineData("lisez-moi.txt", true)]
    [InlineData("script.ps1", false)]
    [InlineData("plan.pdf", false)]
    public void OnlyMarkdownAndPlainTextArePreviewedAsText(string name, bool previewed)
    {
        Assert.Equal(previewed, TextPreviewReader.Previews(name));
    }
}

// The reader against files of a scratch folder: a file far larger than the
// limit is read in bounded bytes, a missing one answers null.
[Trait("Category", "integration")]
public sealed class TextPreviewReaderFileTests
{
    [Fact]
    public void AFileOfSeveralMegabytesGivesABoundedPreview()
    {
        using var scratch = new ScratchFolder();
        string path = Path.Combine(scratch.Path, "journal.txt");
        File.WriteAllText(path, string.Concat(Enumerable.Repeat("une ligne assez longue pour remplir le fichier\n", 100_000)));

        string? preview = TextPreviewReader.Read(path);

        Assert.NotNull(preview);
        Assert.True(preview.Length <= TextPreviewReader.MaxBytes);
        Assert.True(preview.Split('\n').Length <= TextPreviewReader.MaxLines);
    }

    [Fact]
    public void AMissingFileAnswersNull()
    {
        using var scratch = new ScratchFolder();

        Assert.Null(TextPreviewReader.Read(Path.Combine(scratch.Path, "absent.md")));
    }

    [Fact]
    public void TheSampleMarkdownFileHasAPreviewOfSeveralLines()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);

        string? preview = TextPreviewReader.Read(Path.Combine(root, SampleFolder.MarkdownFileName));

        Assert.NotNull(preview);
        Assert.StartsWith("# Lisez-moi", preview);
        Assert.True(preview.Split('\n').Length > 3);
    }
}
