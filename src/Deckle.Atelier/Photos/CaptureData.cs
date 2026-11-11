namespace Deckle.Atelier;

// An exposure time as the camera wrote it, a fraction of a second: 1/250 s
// stays 1/250 and never becomes 0.004.
public readonly record struct ExposureTime(long Numerator, long Denominator)
{
    public double Seconds => Denominator == 0 ? 0 : (double)Numerator / Denominator;
}

// A place in decimal degrees, north and east positive (WGS 84, as EXIF GPS is).
public readonly record struct GeoPoint(double Latitude, double Longitude);

// What the camera recorded, for the "Prise de vue" section of the details
// panel (12.4): date taken, camera, lens, exposure, and the place when the file
// holds one. Every field is optional: a phone photo has no lens name, an old
// scan has nothing at all.
public sealed record CaptureData
{
    public static readonly CaptureData Empty = new();

    // The camera's clock, as it read when the shutter fired. EXIF stores a
    // local time without a zone; TakenOffset holds the zone when the camera
    // wrote one (OffsetTimeOriginal), so a renaming never shifts a date.
    public DateTime? Taken { get; init; }
    public TimeSpan? TakenOffset { get; init; }

    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
    public string? Lens { get; init; }

    public ExposureTime? Exposure { get; init; }
    public double? FNumber { get; init; }
    public int? Iso { get; init; }
    public double? FocalLengthMm { get; init; }

    public GeoPoint? Place { get; init; }

    public bool IsEmpty => this == Empty;
}

public enum CaptureReadFailure
{
    None,
    NotFound,
    AccessDenied,
    // The bytes are not a format the reader knows: a renamed file, or a raw
    // format the reader does not parse.
    UnknownFormat,
    Unreadable,
}

public sealed record CaptureReading(CaptureData Data, CaptureReadFailure Failure)
{
    public bool Succeeded => Failure == CaptureReadFailure.None;

    public static CaptureReading Failed(CaptureReadFailure failure) => new(CaptureData.Empty, failure);
}
