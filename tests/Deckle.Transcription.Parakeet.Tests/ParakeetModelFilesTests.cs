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

    private const ParakeetEncoderPrecision Fp32 = ParakeetEncoderPrecision.Fp32;
    private const ParakeetEncoderPrecision Fp16 = ParakeetEncoderPrecision.Fp16;

    // What the loader opens at FP32: the external weight files are ONNX Runtime's business.
    private static readonly string[] Fp32Files =
    [
        "vocab.txt",
        Path.Combine("fp32", "encoder-model.onnx"),
        Path.Combine("fp32", "decoder_joint-model.onnx"),
    ];

    private static readonly string[] Fp16Files =
    [
        "vocab.txt",
        Path.Combine("fp16", "encoder-model.fp16.onnx"),
        Path.Combine("fp32", "decoder_joint-model.onnx"),
    ];

    [Fact]
    public void EmptyDirectoryIsNotComplete()
    {
        Assert.False(new ParakeetModelFiles(_root).IsCompleteFor(Fp32));
    }

    [Fact]
    public void MissingDirectoryIsNotComplete()
    {
        Assert.False(new ParakeetModelFiles(Path.Combine(_root, "absent")).IsCompleteFor(Fp32));
    }

    [Fact]
    public void TheFilesTheLoaderOpensMakeItComplete()
    {
        foreach (string file in Fp32Files) Touch(file);

        Assert.True(new ParakeetModelFiles(_root).IsCompleteFor(Fp32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnyMissingFileTheLoaderOpensBreaksCompleteness(int missingIndex)
    {
        for (int i = 0; i < Fp32Files.Length; i++)
        {
            if (i != missingIndex) Touch(Fp32Files[i]);
        }

        Assert.False(new ParakeetModelFiles(_root).IsCompleteFor(Fp32));
    }

    [Fact]
    public void TheFp16EncoderIsRequiredAtFp16AndTheFp32OneIsNot()
    {
        foreach (string file in Fp32Files) Touch(file);
        var files = new ParakeetModelFiles(_root);
        Assert.False(files.HasFp16Encoder);
        Assert.False(files.IsCompleteFor(Fp16));

        Touch(Path.Combine("fp16", "encoder-model.fp16.onnx"));

        Assert.True(files.HasFp16Encoder);
        Assert.True(files.IsCompleteFor(Fp16));
        Assert.True(files.IsCompleteFor(Fp32));
    }

    [Fact]
    public void AtFp16TheFp32EncoderIsNotNeeded()
    {
        foreach (string file in Fp16Files) Touch(file);

        var files = new ParakeetModelFiles(_root);
        Assert.True(files.IsCompleteFor(Fp16));
        Assert.False(files.IsCompleteFor(Fp32));
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
        Assert.Equal(Path.Combine(_root, "fp32", "encoder-model.onnx.data.000"), files.EncoderFp32Data000);
        Assert.Equal(Path.Combine(_root, "fp32", "encoder-model.onnx.data.001"), files.EncoderFp32Data001);
        Assert.Equal(Path.Combine(_root, "fp32", "decoder_joint-model.onnx.data"), files.DecoderJointData);
    }
}
