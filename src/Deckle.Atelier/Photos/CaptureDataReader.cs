using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Deckle.Atelier;

// Reads what the camera recorded in a photo. The only file of the library that
// touches MetadataExtractor, so replacing the parser is one file.
//
// Why MetadataExtractor and not Windows' WIC: WIC reads a raw file only through
// a codec, and for most raw formats that codec is the Store's optional Raw
// Image Extension; without it a NEF yields nothing. MetadataExtractor parses
// the headers itself (EXIF, XMP, maker notes; TIFF-based raws, CR3, RAF, HEIF)
// without decoding a pixel, so reading 400 raws is fast and needs nothing
// installed. Apache-2.0, plain .NET.
//
// The values are looked up across every EXIF directory the file holds, in the
// order the parser returns them: a raw often carries several IFDs, and the
// tags a camera writes land in one or the other.
//
// Privacy (spec, Behaviours): what this returns stays in the process. No event
// carries a value read here; a failure logs the extension and the error type.
public static class CaptureDataReader
{
    // EXIF tag numbers (EXIF 2.32, CIPA DC-008). Spelled here rather than taken
    // from the parser's constants so the meaning is read where it is used.
    private const int TagMake = 0x010F;
    private const int TagModel = 0x0110;
    private const int TagExposureTime = 0x829A;
    private const int TagFNumber = 0x829D;
    private const int TagIso = 0x8827;
    private const int TagDateTimeOriginal = 0x9003;
    private const int TagDateTimeDigitized = 0x9004;
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagFocalLength = 0x920A;
    private const int TagLensMake = 0xA433;
    private const int TagLensModel = 0xA434;

    public static CaptureReading Read(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail(path, CaptureReadFailure.NotFound, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fail(path, CaptureReadFailure.AccessDenied, ex);
        }
        catch (ImageProcessingException ex)
        {
            return Fail(path, CaptureReadFailure.UnknownFormat, ex);
        }
        catch (IOException ex)
        {
            return Fail(path, CaptureReadFailure.Unreadable, ex);
        }

        return new CaptureReading(FromDirectories(directories), CaptureReadFailure.None);
    }

    internal static CaptureData FromDirectories(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        ExifDirectoryBase[] exif = [.. directories.OfType<ExifDirectoryBase>()];

        DateTime? taken = Date(exif, TagDateTimeOriginal) ?? Date(exif, TagDateTimeDigitized);
        string? lensModel = Text(exif, TagLensModel);
        string? lensMake = Text(exif, TagLensMake);

        return new CaptureData
        {
            Taken = taken,
            TakenOffset = taken is null ? null : Offset(Text(exif, TagOffsetTimeOriginal)),
            CameraMake = Text(exif, TagMake),
            CameraModel = Text(exif, TagModel),
            Lens = lensModel is not null && lensMake is not null && !lensModel.StartsWith(lensMake, StringComparison.OrdinalIgnoreCase)
                ? $"{lensMake} {lensModel}"
                : lensModel,
            Exposure = Fraction(exif, TagExposureTime) is { } t && t.Numerator > 0 ? new ExposureTime(t.Numerator, t.Denominator) : null,
            FNumber = Positive(exif, TagFNumber),
            Iso = Integer(exif, TagIso),
            FocalLengthMm = Positive(exif, TagFocalLength),
            Place = Place(directories),
        };
    }

    private static string? Text(ExifDirectoryBase[] exif, int tag)
    {
        foreach (ExifDirectoryBase d in exif)
        {
            string? value = d.GetString(tag)?.Trim().TrimEnd('\0').Trim();
            if (!string.IsNullOrEmpty(value)) return value;
        }
        return null;
    }

    private static DateTime? Date(ExifDirectoryBase[] exif, int tag)
    {
        foreach (ExifDirectoryBase d in exif)
        {
            // EXIF writes "yyyy:MM:dd HH:mm:ss"; a camera with no clock set
            // writes zeros or blanks, which fail to parse and count as absent.
            string? raw = d.GetString(tag)?.Trim();
            if (raw is not null && DateTime.TryParseExact(raw, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
                return value;
        }
        return null;
    }

    private static Rational? Fraction(ExifDirectoryBase[] exif, int tag)
    {
        foreach (ExifDirectoryBase d in exif)
        {
            if (d.TryGetRational(tag, out Rational value) && value.Denominator != 0) return value;
        }
        return null;
    }

    private static double? Positive(ExifDirectoryBase[] exif, int tag) =>
        Fraction(exif, tag) is { } r && r.ToDouble() > 0 ? r.ToDouble() : null;

    private static int? Integer(ExifDirectoryBase[] exif, int tag)
    {
        foreach (ExifDirectoryBase d in exif)
        {
            if (d.TryGetInt32(tag, out int value) && value > 0) return value;
        }
        return null;
    }

    // "+02:00" or "-05:30", as EXIF 2.31 writes OffsetTimeOriginal.
    private static TimeSpan? Offset(string? raw)
    {
        if (raw is null || raw.Length != 6 || (raw[0] != '+' && raw[0] != '-')) return null;
        if (!TimeSpan.TryParseExact(raw[1..], @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan span)) return null;
        return raw[0] == '-' ? span.Negate() : span;
    }

    // A camera without a fix sometimes writes 0° 0'; that point in the Gulf of
    // Guinea is read as no place.
    private static GeoPoint? Place(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        foreach (GpsDirectory gps in directories.OfType<GpsDirectory>())
        {
            if (gps.TryGetGeoLocation(out GeoLocation location) && !(location.Latitude == 0 && location.Longitude == 0))
                return new GeoPoint(location.Latitude, location.Longitude);
        }
        return null;
    }

    private static CaptureReading Fail(string path, CaptureReadFailure failure, Exception ex)
    {
        DeckleAtelierSource.Log.CaptureDataReadFailed(Path.GetExtension(path).ToLowerInvariant(), failure.ToString(), ex.GetType().Name);
        return CaptureReading.Failed(failure);
    }
}
