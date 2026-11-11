using Xunit;

namespace Deckle.Atelier.Tests;

// The self-check's shoot must read as a shoot: photos the tool lists, with
// the capture data the panel shows, and one scan that holds none.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class SamplePhotosTests
{
    [Fact]
    public void TheSampleShootIsListedAsPhotosWithTheirCaptureData()
    {
        using var scratch = new ScratchFolder();
        string shoot = SamplePhotos.Write(scratch.Path);

        PhotoFolderResult listing = PhotoFolder.List(shoot);
        Assert.Equal(SamplePhotos.Names.Count, listing.Photos.Count);
        Assert.Equal(2, listing.Photos.Count(p => p.IsRaw));

        CaptureReading first = CaptureDataReader.Read(Path.Combine(shoot, "DSC_0201.JPG"));
        Assert.True(first.Succeeded);
        Assert.Equal(new DateTime(2026, 6, 14, 14, 5, 7), first.Data.Taken);
        Assert.Equal("Kestrel K2", CaptureDataFormat.Camera(first.Data));
        Assert.NotNull(first.Data.Place);

        CaptureReading raw = CaptureDataReader.Read(Path.Combine(shoot, "DSC_0217.NEF"));
        Assert.True(raw.Succeeded);
        Assert.Equal(400, raw.Data.Iso);
    }

    [Fact]
    public void TheSampleJpegIsABaselineFrameOfItsSize()
    {
        using var scratch = new ScratchFolder();
        string shoot = SamplePhotos.Write(scratch.Path);
        byte[] bytes = File.ReadAllBytes(Path.Combine(shoot, "Scan-album-1987.jpg"));

        Assert.Equal([0xFF, 0xD8], bytes[..2]);
        Assert.Equal([0xFF, 0xD9], bytes[^2..]);
        int sof = bytes.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        Assert.True(sof > 0);
        Assert.Equal(32, (bytes[sof + 5] << 8) | bytes[sof + 6]);
        Assert.Equal(48, (bytes[sof + 7] << 8) | bytes[sof + 8]);
    }
}
