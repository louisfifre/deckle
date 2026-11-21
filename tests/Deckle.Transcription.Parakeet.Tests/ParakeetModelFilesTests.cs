using Xunit;

namespace Deckle.Transcription.Parakeet.Tests;

[Trait("Category", "integration")]
public sealed class ParakeetModelFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-parakeet-{Guid.NewGuid():N}");

    public ParakeetModelFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Touch(string relativePath)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    private static readonly string[] RequiredFiles =
    [
        Path.Combine("fp32", "encoder-model.onnx"),
        Path.Combine("fp32", "decoder_joint-model.onnx"),
        "vocab.txt",
    ];

    [Fact]
    public void EmptyDirectoryIsNotComplete()
    {
        Assert.False(new ParakeetModelFiles(_root).IsComplete);
    }

    [Fact]
    public void MissingDirectoryIsNotComplete()
    {
        Assert.False(new ParakeetModelFiles(Path.Combine(_root, "absent")).IsComplete);
    }

    [Fact]
    public void AllRequiredFilesMakeItComplete()
    {
        foreach (string file in RequiredFiles) Touch(file);

        Assert.True(new ParakeetModelFiles(_root).IsComplete);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnyMissingRequiredFileBreaksCompleteness(int missingIndex)
    {
        for (int i = 0; i < RequiredFiles.Length; i++)
        {
            if (i != missingIndex) Touch(RequiredFiles[i]);
        }

        Assert.False(new ParakeetModelFiles(_root).IsComplete);
    }

    [Fact]
    public void Fp16EncoderIsOptionalForCompleteness()
    {
        foreach (string file in RequiredFiles) Touch(file);
        var files = new ParakeetModelFiles(_root);
        Assert.False(files.HasFp16Encoder);

        Touch(Path.Combine("fp16", "encoder-model.fp16.onnx"));

        Assert.True(files.HasFp16Encoder);
        Assert.True(files.IsComplete);
    }

    [Fact]
    public void TheMelFrontEndIsShippedWithTheAppNotReadFromTheModelFolder()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "parakeet-nemo128.onnx"), ParakeetModelFiles.BundledPreprocessor);
        Assert.True(File.Exists(ParakeetModelFiles.BundledPreprocessor), "The build must copy the bundled mel graph beside the assembly.");
        // The shipped graph is onnx-asr 0.12.0's, not the model repository's: its hash is recorded in NOTICE.md.
        Assert.Equal(
            "5B4A84C52EEAA615DC46D781CC7E4598F9B432184831D57493C48ADCD01371A9",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ParakeetModelFiles.BundledPreprocessor))));
    }

    [Fact]
    public void PathsFollowTheModelFolderLayout()
    {
        var files = new ParakeetModelFiles(_root);

        Assert.Equal(Path.Combine(_root, "fp16", "encoder-model.fp16.onnx"), files.EncoderFp16);
        Assert.Equal(Path.Combine(_root, "vocab.txt"), files.Vocabulary);
    }
}
