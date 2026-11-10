using System.Buffers.Binary;
using System.Text;

namespace Deckle.Atelier.Tests;

// Writes, in code, the capture data a camera would: a TIFF structure with an
// IFD0 (make, model), an EXIF IFD (date, exposure, lens) and a GPS IFD. As a
// JPEG it sits in an APP1 "Exif" segment; as a TIFF-based raw (NEF, DNG, ARW,
// CR2 are TIFF files) it is the file itself. No real photo is ever read: the
// tests stand on these bytes alone.
internal sealed record ExifSample
{
    public string? Make { get; init; } = "KESTREL";
    public string? Model { get; init; } = "Kestrel K2";
    public string? DateTimeOriginal { get; init; } = "2026:06:14 15:42:07";
    public string? OffsetTimeOriginal { get; init; } = "+02:00";
    public (uint Num, uint Den)? ExposureTime { get; init; } = (1, 250);
    public (uint Num, uint Den)? FNumber { get; init; } = (28, 10);
    public ushort? Iso { get; init; } = 400;
    public (uint Num, uint Den)? FocalLength { get; init; } = (35, 1);
    public string? LensMake { get; init; }
    public string? LensModel { get; init; } = "35 mm f/1.8";
    // Decimal degrees, south and west negative.
    public (double Lat, double Lon)? Place { get; init; } = (45.7640, 4.8357);

    public static readonly ExifSample Blank = new()
    {
        Make = null, Model = null, DateTimeOriginal = null, OffsetTimeOriginal = null, ExposureTime = null,
        FNumber = null, Iso = null, FocalLength = null, LensModel = null, Place = null,
    };

    public byte[] Jpeg()
    {
        byte[] tiff = Tiff();
        using var s = new MemoryStream();
        s.Write([0xFF, 0xD8]);                         // SOI
        s.Write([0xFF, 0xE1]);                         // APP1
        int length = 2 + 6 + tiff.Length;
        s.Write([(byte)(length >> 8), (byte)length]);
        s.Write("Exif\0\0"u8);
        s.Write(tiff);
        s.Write([0xFF, 0xD9]);                         // EOI
        return s.ToArray();
    }

    public string WriteJpeg(string path)
    {
        File.WriteAllBytes(path, Jpeg());
        return path;
    }

    public string WriteTiff(string path)
    {
        File.WriteAllBytes(path, Tiff());
        return path;
    }

    // ── TIFF layout, little-endian ("II") ─────────────────────────────────

    private const ushort Ascii = 2, Short = 3, Long = 4, Rational = 5;

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    public byte[] Tiff()
    {
        var ifd0 = new List<Entry>();
        var exif = new List<Entry>();
        var gps = new List<Entry>();

        AddAscii(ifd0, 0x010F, Make);
        AddAscii(ifd0, 0x0110, Model);

        AddRational(exif, 0x829A, ExposureTime);
        AddRational(exif, 0x829D, FNumber);
        if (Iso is { } iso) exif.Add(new Entry(0x8827, Short, 1, U16(iso)));
        AddAscii(exif, 0x9003, DateTimeOriginal);
        AddAscii(exif, 0x9011, OffsetTimeOriginal);
        AddRational(exif, 0x920A, FocalLength);
        AddAscii(exif, 0xA433, LensMake);
        AddAscii(exif, 0xA434, LensModel);

        if (Place is { } p)
        {
            AddAscii(gps, 0x0001, p.Lat >= 0 ? "N" : "S");
            gps.Add(new Entry(0x0002, Rational, 3, Degrees(Math.Abs(p.Lat))));
            AddAscii(gps, 0x0003, p.Lon >= 0 ? "E" : "W");
            gps.Add(new Entry(0x0004, Rational, 3, Degrees(Math.Abs(p.Lon))));
        }

        // Pointers are placeholders of the right size, filled once offsets
        // are known.
        if (exif.Count > 0) ifd0.Add(new Entry(0x8769, Long, 1, new byte[4]));
        if (gps.Count > 0) ifd0.Add(new Entry(0x8825, Long, 1, new byte[4]));
        ifd0.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        uint ifd0Offset = 8;
        uint exifOffset = ifd0Offset + Size(ifd0);
        uint gpsOffset = exifOffset + (exif.Count > 0 ? Size(exif) : 0);
        Point(ifd0, 0x8769, exifOffset);
        Point(ifd0, 0x8825, gpsOffset);

        using var s = new MemoryStream();
        s.Write("II"u8);
        s.Write(U16(42));
        s.Write(U32(ifd0Offset));
        WriteIfd(s, ifd0, ifd0Offset);
        if (exif.Count > 0) WriteIfd(s, exif, exifOffset);
        if (gps.Count > 0) WriteIfd(s, gps, gpsOffset);
        return s.ToArray();
    }

    private static uint Size(List<Entry> entries) =>
        (uint)(2 + 12 * entries.Count + 4 + entries.Where(e => e.Value.Length > 4).Sum(e => Even(e.Value.Length)));

    private static void WriteIfd(MemoryStream s, List<Entry> entries, uint offset)
    {
        uint data = offset + (uint)(2 + 12 * entries.Count + 4);
        var tail = new MemoryStream();
        s.Write(U16((ushort)entries.Count));
        foreach (Entry e in entries)
        {
            s.Write(U16(e.Tag));
            s.Write(U16(e.Type));
            s.Write(U32(e.Count));
            if (e.Value.Length <= 4)
            {
                var inline = new byte[4];
                e.Value.CopyTo(inline, 0);
                s.Write(inline);
            }
            else
            {
                s.Write(U32(data + (uint)tail.Length));
                tail.Write(e.Value);
                if (e.Value.Length % 2 == 1) tail.WriteByte(0);
            }
        }
        s.Write(U32(0)); // no next IFD
        s.Write(tail.ToArray());
    }

    private static void Point(List<Entry> entries, ushort tag, uint offset)
    {
        int i = entries.FindIndex(e => e.Tag == tag);
        if (i >= 0) entries[i] = entries[i] with { Value = U32(offset) };
    }

    private static void AddAscii(List<Entry> entries, ushort tag, string? value)
    {
        if (value is null) return;
        byte[] bytes = Encoding.ASCII.GetBytes(value + "\0");
        entries.Add(new Entry(tag, Ascii, (uint)bytes.Length, bytes));
    }

    private static void AddRational(List<Entry> entries, ushort tag, (uint Num, uint Den)? value)
    {
        if (value is not { } v) return;
        entries.Add(new Entry(tag, Rational, 1, [.. U32(v.Num), .. U32(v.Den)]));
    }

    // Degrees, minutes, seconds as three rationals; seconds to 1/10000.
    private static byte[] Degrees(double value)
    {
        uint d = (uint)value;
        double minutes = (value - d) * 60;
        uint m = (uint)minutes;
        uint sec = (uint)Math.Round((minutes - m) * 60 * 10000);
        return [.. U32(d), .. U32(1), .. U32(m), .. U32(1), .. U32(sec), .. U32(10000)];
    }

    private static int Even(int n) => n + (n % 2);

    private static byte[] U16(ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, v);
        return b;
    }

    private static byte[] U32(uint v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, v);
        return b;
    }
}
