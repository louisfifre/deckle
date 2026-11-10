using Xunit;

namespace Deckle.Atelier.Tests;

// The two rules applied to what the shell hands back: which files may be
// read for a thumbnail, and which bitmaps need their alpha made opaque.
[Trait("Category", "unit")]
public sealed class ShellPixelRulesTests
{
    [Theory]
    [InlineData(FileAttributes.Normal, true)]
    [InlineData(FileAttributes.Archive | FileAttributes.ReadOnly, true)]
    [InlineData(FileAttributes.Archive | (FileAttributes)0x80000, true)]   // pinned: kept on the disk
    [InlineData(FileAttributes.Offline, false)]
    [InlineData((FileAttributes)0x40000, false)]                           // recall on open
    [InlineData(FileAttributes.Archive | (FileAttributes)0x400000, false)] // recall on data access (OneDrive online-only)
    public void OnlyAFileWhoseContentIsOnTheDiskIsLocal(FileAttributes attributes, bool local)
    {
        Assert.Equal(local, FileContentPresence.IsLocal(attributes));
    }

    [Fact]
    public void ABitmapWithoutAlphaIsMadeOpaque()
    {
        byte[] pixels = [10, 20, 30, 0, 0, 0, 0, 0];
        ShellImagePixels.OpaqueIfNoAlpha(pixels);
        Assert.Equal([10, 20, 30, 255, 0, 0, 0, 255], pixels);
    }

    [Fact]
    public void ABitmapWithAlphaPassesUntouched()
    {
        byte[] pixels = [10, 20, 30, 128, 0, 0, 0, 0];
        ShellImagePixels.OpaqueIfNoAlpha(pixels);
        Assert.Equal([10, 20, 30, 128, 0, 0, 0, 0], pixels);
    }

    [Fact]
    public void AnEmptyBitmapStaysTransparent()
    {
        byte[] pixels = new byte[8];
        ShellImagePixels.OpaqueIfNoAlpha(pixels);
        Assert.All(pixels, b => Assert.Equal(0, b));
    }
}
