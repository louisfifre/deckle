using System.Runtime.InteropServices;
using Xunit;

namespace Deckle.Transcription.Whisper.Tests;

public sealed class PeImportsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckle-pe-imports-{Guid.NewGuid():N}");

    [Fact]
    [Trait("Category", "integration")]
    public void ReadsStaticAndDelayLoadedImportsOfAnImage()
    {
        string image = Path.Combine(_root, "sample.dll");
        TestPeImage.Write(image, ["KERNEL32.dll", "libgomp-1.dll"], ["vulkan-1.dll"]);

        Assert.Equal(["KERNEL32.dll", "libgomp-1.dll", "vulkan-1.dll"], PeImports.ReadImportedDllNames(image));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AnImageWithoutImportsReadsAsEmpty()
    {
        string image = Path.Combine(_root, "bare.dll");
        TestPeImage.Write(image);

        Assert.Empty(PeImports.ReadImportedDllNames(image));
    }

    [Fact]
    [Trait("Category", "integration")]
    public void AFileThatIsNotAnImageIsRefusedByName()
    {
        string text = Path.Combine(_root, "text.dll");
        Directory.CreateDirectory(_root);
        File.WriteAllText(text, "not a DLL");

        var ex = Assert.Throws<BadImageFormatException>(() => PeImports.ReadImportedDllNames(text));
        Assert.Contains("text.dll is not a PE image", ex.Message);
    }

    // The fixture is a DLL Windows accepts, so what the reader sees is the
    // import table the loader resolves.
    [Fact]
    [Trait("Category", "integration")]
    public void TheFixtureLoadsLikeARealDll()
    {
        string image = Path.Combine(_root, "loadable.dll");
        TestPeImage.Write(image, ["KERNEL32.dll"]);

        IntPtr handle = NativeLibrary.Load(image);
        Assert.NotEqual(IntPtr.Zero, handle);
        NativeLibrary.Free(handle);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void ReadsARealSystemDll()
    {
        string kernel32 = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Assert.Contains(PeImports.ReadImportedDllNames(kernel32), n => n.Equals("ntdll.dll", StringComparison.OrdinalIgnoreCase));
    }

    // Regression: a file cut short declares offsets past its end, and the
    // walk threw ArgumentOutOfRangeException, which the loader's diagnosis
    // did not catch, so explaining a failed load threw over it. Every cut of
    // a real image must read or be refused as a bad image, nothing else.
    [Fact]
    [Trait("Category", "regression")]
    public void ATruncatedImageIsRefusedAsABadImageAtEveryLength()
    {
        string whole = Path.Combine(_root, "whole.dll");
        TestPeImage.Write(whole, ["KERNEL32.dll", "libgomp-1.dll"], ["vulkan-1.dll"]);
        byte[] bytes = File.ReadAllBytes(whole);
        string cut = Path.Combine(_root, "cut.dll");

        for (int length = 0; length < bytes.Length; length++)
        {
            File.WriteAllBytes(cut, bytes[..length]);
            try
            {
                PeImports.ReadImportedDllNames(cut);
            }
            catch (BadImageFormatException)
            {
            }
            catch (Exception ex)
            {
                Assert.Fail($"A cut at {length} of {bytes.Length} bytes threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
