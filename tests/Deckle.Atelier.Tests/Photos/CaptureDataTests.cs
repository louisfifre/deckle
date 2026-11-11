using System.Globalization;
using Xunit;

namespace Deckle.Atelier.Tests;

// What the camera recorded reaches the details panel, from a JPEG and from a
// TIFF-based raw, and a file that holds nothing or is not a photo never
// throws.
[Collection("atelier provider singleton")]
[Trait("Category", "integration")]
public sealed class CaptureDataReaderTests
{
    [Fact]
    public void AJpegGivesEveryFieldTheCameraWrote()
    {
        using var scratch = new ScratchFolder();
        string path = new ExifSample().WriteJpeg(Path.Combine(scratch.Path, "DSC_0217.JPG"));

        CaptureReading reading = CaptureDataReader.Read(path);

        Assert.True(reading.Succeeded);
        CaptureData d = reading.Data;
        Assert.Equal(new DateTime(2026, 6, 14, 15, 42, 7), d.Taken);
        Assert.Equal(TimeSpan.FromHours(2), d.TakenOffset);
        Assert.Equal("KESTREL", d.CameraMake);
        Assert.Equal("Kestrel K2", d.CameraModel);
        Assert.Equal("35 mm f/1.8", d.Lens);
        Assert.Equal(new ExposureTime(1, 250), d.Exposure);
        Assert.Equal(2.8, d.FNumber!.Value, 3);
        Assert.Equal(400, d.Iso);
        Assert.Equal(35, d.FocalLengthMm!.Value, 3);
        Assert.Equal(45.7640, d.Place!.Value.Latitude, 4);
        Assert.Equal(4.8357, d.Place!.Value.Longitude, 4);
    }

    // NEF, DNG, ARW and CR2 are TIFF files: the same structure under a raw
    // extension reads the same. Formats that are not TIFF-based (CR3, RAF,
    // ORF's and RW2's own headers) cannot be produced here without a real file.
    [Theory]
    [InlineData("DSC_0217.NEF")]
    [InlineData("IMG_0001.DNG")]
    [InlineData("DSC00001.ARW")]
    public void ATiffBasedRawGivesTheSameFields(string name)
    {
        using var scratch = new ScratchFolder();
        string path = new ExifSample().WriteTiff(Path.Combine(scratch.Path, name));

        CaptureReading reading = CaptureDataReader.Read(path);

        Assert.True(reading.Succeeded);
        Assert.Equal(new DateTime(2026, 6, 14, 15, 42, 7), reading.Data.Taken);
        Assert.Equal("Kestrel K2", reading.Data.CameraModel);
        Assert.Equal(new ExposureTime(1, 250), reading.Data.Exposure);
    }

    [Fact]
    public void APhotoWithoutCaptureDataReadsAsEmpty()
    {
        using var scratch = new ScratchFolder();
        string path = ExifSample.Blank.WriteJpeg(Path.Combine(scratch.Path, "scan.jpg"));

        CaptureReading reading = CaptureDataReader.Read(path);

        Assert.True(reading.Succeeded);
        Assert.True(reading.Data.IsEmpty);
    }

    [Fact]
    public void APlaceSouthAndWestIsNegative()
    {
        using var scratch = new ScratchFolder();
        string path = new ExifSample { Place = (-33.8568, -70.6483) }.WriteJpeg(Path.Combine(scratch.Path, "a.jpg"));

        GeoPoint place = CaptureDataReader.Read(path).Data.Place!.Value;

        Assert.Equal(-33.8568, place.Latitude, 4);
        Assert.Equal(-70.6483, place.Longitude, 4);
    }

    [Fact]
    public void ACameraWithoutAFixHasNoPlace()
    {
        using var scratch = new ScratchFolder();
        string path = new ExifSample { Place = (0, 0) }.WriteJpeg(Path.Combine(scratch.Path, "a.jpg"));

        Assert.Null(CaptureDataReader.Read(path).Data.Place);
    }

    [Fact]
    public void AClockNeverSetCountsAsNoDate()
    {
        using var scratch = new ScratchFolder();
        string path = new ExifSample { DateTimeOriginal = "0000:00:00 00:00:00" }.WriteJpeg(Path.Combine(scratch.Path, "a.jpg"));

        CaptureData d = CaptureDataReader.Read(path).Data;

        Assert.Null(d.Taken);
        Assert.Null(d.TakenOffset);
    }

    [Fact]
    public void BytesThatAreNotAPhotoAreAnUnknownFormat()
    {
        using var scratch = new ScratchFolder();
        string path = scratch.File("not a raw at all", "fake.NEF");

        Assert.Equal(CaptureReadFailure.UnknownFormat, CaptureDataReader.Read(path).Failure);
    }

    [Fact]
    public void AMissingFileIsNotFound()
    {
        using var scratch = new ScratchFolder();

        Assert.Equal(CaptureReadFailure.NotFound, CaptureDataReader.Read(Path.Combine(scratch.Path, "gone.jpg")).Failure);
    }
}

// The panel's rows read as the boards write them (12.4).
[Trait("Category", "unit")]
public sealed class CaptureDataFormatTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly CaptureData Sample = new()
    {
        Taken = new DateTime(2026, 6, 14, 15, 42, 7),
        CameraMake = "KESTREL",
        CameraModel = "Kestrel K2",
        Lens = "35 mm f/1,8",
        Exposure = new ExposureTime(1, 250),
        FNumber = 2.8,
        Iso = 400,
        Place = new GeoPoint(45.7640, 4.8357),
    };

    [Fact]
    public void TheDateReadsLikeTheBoard() =>
        Assert.Equal("dim. 14 juin 2026, 15:42", CaptureDataFormat.Taken(Sample, French));

    [Fact]
    public void TheMakeIsLeftOutWhenTheModelNamesIt() =>
        Assert.Equal("Kestrel K2", CaptureDataFormat.Camera(Sample));

    [Fact]
    public void TheMakeComesFirstWhenTheModelDoesNotNameIt() =>
        Assert.Equal("Canon EOS R6", CaptureDataFormat.Camera(new CaptureData { CameraMake = "Canon", CameraModel = "EOS R6" }));

    [Fact]
    public void TheExposureReadsLikeTheBoard() =>
        Assert.Equal("1/250 s · f/2,8 · ISO 400", CaptureDataFormat.Exposure(Sample, French));

    [Fact]
    public void ALongExposureReadsInSeconds() =>
        Assert.Equal("2 s", CaptureDataFormat.Exposure(new CaptureData { Exposure = new ExposureTime(2, 1) }, French));

    [Fact]
    public void ThePlaceReadsLikeTheBoard() =>
        Assert.Equal("45,7640° N, 4,8357° E", CaptureDataFormat.Place(Sample, French));

    [Fact]
    public void AFocalLengthStandsInForAMissingLensName() =>
        Assert.Equal("26 mm", CaptureDataFormat.Lens(new CaptureData { FocalLengthMm = 26 }, French));

    [Fact]
    public void AValueTheFileDoesNotHoldIsNull()
    {
        Assert.Null(CaptureDataFormat.Taken(CaptureData.Empty, French));
        Assert.Null(CaptureDataFormat.Exposure(CaptureData.Empty, French));
        Assert.Null(CaptureDataFormat.Place(CaptureData.Empty, French));
        Assert.Null(CaptureDataFormat.Camera(CaptureData.Empty));
    }
}
