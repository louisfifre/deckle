using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Deckle.Atelier;

// Photos made from nothing, for the self-check to show the photo tool on: a
// shoot of small JPEGs, each one plain colour, with the capture data a camera
// writes (date, camera, lens, exposure, sometimes a place), two raws that hold
// only their capture data, and one scan that holds none. Nothing comes from
// the person's disk and no real photo is ever read (spec, Open 6: a session
// reads nothing of the photos); the camera is the boards' made-up one.
//
// The pixels are a real baseline JPEG, written here rather than through an
// imaging library: one flat colour per photo needs only the DC coefficient of
// each 8×8 block, so the whole encoder is a quantization table of ones, two
// tiny Huffman tables and a bit writer. Any decoder, the shell's thumbnails
// included, then shows the photo as its colour (ITU-T T.81, baseline DCT).
public static class SamplePhotos
{
    public const string ShootFolder = "2026-06-14 Mariage";

    private const int Width = 48;
    private const int Height = 32;

    private sealed record Shot(string Name, (byte R, byte G, byte B) Colour, int Minute,
                               (uint Num, uint Den) Exposure, (uint Num, uint Den) FNumber, ushort Iso,
                               bool WithPlace = false, bool Raw = false, bool Blank = false);

    private static readonly DateTime Start = new(2026, 6, 14, 14, 5, 0);

    // The colours of board 12.4: greens, browns, slates, a few dark frames.
    private static readonly Shot[] Shots =
    [
        new("DSC_0201.JPG", (92, 110, 94), 0, (1, 250), (28, 10), 400, WithPlace: true),
        new("DSC_0202.JPG", (124, 106, 86), 1, (1, 250), (28, 10), 400),
        new("DSC_0203.JPG", (36, 40, 48), 2, (1, 60), (18, 10), 1600),
        new("DSC_0204.JPG", (140, 124, 100), 4, (1, 320), (40, 10), 200),
        new("DSC_0205.JPG", (110, 94, 100), 5, (1, 200), (28, 10), 400),
        new("DSC_0206.JPG", (86, 116, 98), 7, (1, 250), (28, 10), 400, WithPlace: true),
        new("DSC_0207.JPG", (128, 110, 88), 9, (1, 250), (20, 10), 320, WithPlace: true),
        new("DSC_0208.JPG", (34, 38, 46), 10, (1, 50), (18, 10), 3200),
        new("DSC_0209.JPG", (106, 122, 98), 12, (1, 500), (56, 10), 100),
        new("DSC_0210.JPG", (128, 100, 80), 13, (1, 250), (28, 10), 400),
        new("DSC_0211.JPG", (92, 102, 120), 15, (1, 160), (28, 10), 400),
        new("DSC_0212.JPG", (140, 128, 104), 16, (1, 250), (28, 10), 400),
        new("DSC_0213.JPG", (110, 118, 100), 18, (1, 250), (28, 10), 400),
        new("DSC_0214.JPG", (122, 100, 96), 20, (1, 125), (20, 10), 800),
        new("DSC_0215.NEF", (0, 0, 0), 21, (1, 250), (28, 10), 400, WithPlace: true, Raw: true),
        new("DSC_0216.JPG", (62, 50, 40), 23, (1, 30), (18, 10), 6400),
        new("DSC_0217.NEF", (0, 0, 0), 24, (1, 250), (28, 10), 400, WithPlace: true, Raw: true),
        new("DSC_0218.JPG", (96, 106, 120), 26, (1, 250), (28, 10), 400),
        new("DSC_0219.JPG", (122, 114, 94), 27, (1, 400), (40, 10), 200),
        new("DSC_0220.JPG", (84, 104, 98), 29, (1, 250), (28, 10), 400),
        new("DSC_0221.JPG", (112, 98, 88), 31, (1, 250), (28, 10), 400),
        new("DSC_0222.JPG", (130, 120, 98), 33, (1, 1000), (80, 10), 100),
        new("DSC_0223.JPG", (78, 96, 88), 35, (1, 250), (28, 10), 400),
        new("Scan-album-1987.jpg", (150, 132, 108), 0, (0, 0), (0, 0), 0, Blank: true),
    ];

    // Writes the shoot into <folder>\2026-06-14 Mariage and returns that
    // folder. Files a previous run left there are replaced.
    public static string Write(string folder)
    {
        string shoot = Path.Combine(folder, ShootFolder);
        Directory.CreateDirectory(shoot);
        foreach (Shot shot in Shots)
        {
            string path = Path.Combine(shoot, shot.Name);
            byte[] exif = shot.Blank ? [] : Tiff(shot);
            File.WriteAllBytes(path, shot.Raw ? exif : Jpeg(shot.Colour, exif));
            File.SetLastWriteTime(path, shot.Blank ? new DateTime(2019, 3, 2, 18, 0, 0) : Start.AddMinutes(shot.Minute + 1));
        }
        return shoot;
    }

    public static IReadOnlyList<string> Names => [.. Shots.Select(s => s.Name)];

    // ── JPEG ─────────────────────────────────────────────────────────────

    private static byte[] Jpeg((byte R, byte G, byte B) colour, byte[] exif)
    {
        using var s = new MemoryStream();
        s.Write([0xFF, 0xD8]);                                   // SOI
        if (exif.Length > 0)
        {
            Segment(s, 0xE1, [.. "Exif\0\0"u8.ToArray(), .. exif]); // APP1
        }

        // One quantization table of ones: the DC coefficient passes as is.
        Segment(s, 0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]);

        // Baseline frame: 8-bit, three components (Y, Cb, Cr), no subsampling.
        Segment(s, 0xC0,
        [
            8, Height >> 8, Height & 0xFF, Width >> 8, Width & 0xFF, 3,
            1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0,
        ]);

        // DC table 0: the twelve size categories, each a 4-bit code equal to
        // the category. AC table 0: end-of-block alone, the 1-bit code 0.
        byte[] dcCounts = new byte[16];
        dcCounts[3] = 12;
        Segment(s, 0xC4, [0x00, .. dcCounts, .. Enumerable.Range(0, 12).Select(i => (byte)i)]);
        byte[] acCounts = new byte[16];
        acCounts[0] = 1;
        Segment(s, 0xC4, [0x10, .. acCounts, 0x00]);

        Segment(s, 0xDA, [3, 1, 0x00, 2, 0x00, 3, 0x00, 0, 63, 0]);
        s.Write(Scan(colour));
        s.Write([0xFF, 0xD9]);                                   // EOI
        return s.ToArray();
    }

    // Every block of a component holds the same DC value, 8 times the level-
    // shifted sample (the 2-D DCT of a flat 8×8 block, divided by a
    // quantizer of 1); the first block carries it, the next ones a difference
    // of 0.
    private static byte[] Scan((byte R, byte G, byte B) c)
    {
        double y = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        double cb = 128 - 0.168736 * c.R - 0.331264 * c.G + 0.5 * c.B;
        double cr = 128 + 0.5 * c.R - 0.418688 * c.G - 0.081312 * c.B;
        int[] dc = [.. new[] { y, cb, cr }.Select(v => (int)Math.Round(8 * (v - 128)))];

        var bits = new BitWriter();
        int[] previous = new int[3];
        for (int block = 0; block < (Width / 8) * (Height / 8); block++)
        {
            for (int component = 0; component < 3; component++)
            {
                int diff = dc[component] - previous[component];
                previous[component] = dc[component];
                int category = diff == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(diff));
                bits.Write(category, 4);
                if (category > 0) bits.Write(diff > 0 ? diff : diff + (1 << category) - 1, category);
                bits.Write(0, 1);                                // end of block
            }
        }
        return bits.Finish();
    }

    private static void Segment(MemoryStream s, byte marker, byte[] payload)
    {
        int length = payload.Length + 2;
        s.Write([0xFF, marker, (byte)(length >> 8), (byte)length]);
        s.Write(payload);
    }

    // Most significant bit first; a 0xFF byte is followed by a 0x00 so it is
    // not read as a marker; the last byte is padded with ones.
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _current;
        private int _count;

        public void Write(int value, int length)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                _current = (_current << 1) | ((value >> i) & 1);
                if (++_count == 8) Flush();
            }
        }

        public byte[] Finish()
        {
            while (_count != 0) Write(1, 1);
            return [.. _bytes];
        }

        private void Flush()
        {
            _bytes.Add((byte)_current);
            if (_current == 0xFF) _bytes.Add(0x00);
            _current = 0;
            _count = 0;
        }
    }

    // ── EXIF (a little-endian TIFF structure) ────────────────────────────

    private const ushort Ascii = 2, Short = 3, Long = 4, Rational = 5;

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    private static byte[] Tiff(Shot shot)
    {
        var ifd0 = new List<Entry>();
        var exif = new List<Entry>();
        var gps = new List<Entry>();

        AddAscii(ifd0, 0x010F, "KESTREL");
        AddAscii(ifd0, 0x0110, "Kestrel K2");

        exif.Add(new Entry(0x829A, Rational, 1, [.. U32(shot.Exposure.Num), .. U32(shot.Exposure.Den)]));
        exif.Add(new Entry(0x829D, Rational, 1, [.. U32(shot.FNumber.Num), .. U32(shot.FNumber.Den)]));
        exif.Add(new Entry(0x8827, Short, 1, U16(shot.Iso)));
        AddAscii(exif, 0x9003, Start.AddMinutes(shot.Minute).AddSeconds(7).ToString("yyyy:MM:dd HH:mm:ss"));
        AddAscii(exif, 0x9011, "+02:00");
        exif.Add(new Entry(0x920A, Rational, 1, [.. U32(35), .. U32(1)]));
        AddAscii(exif, 0xA434, "35 mm f/1.8");

        if (shot.WithPlace)
        {
            AddAscii(gps, 0x0001, "N");
            gps.Add(new Entry(0x0002, Rational, 3, Degrees(45.7640 + shot.Minute * 0.0001)));
            AddAscii(gps, 0x0003, "E");
            gps.Add(new Entry(0x0004, Rational, 3, Degrees(4.8357)));
        }

        // Pointers are placeholders of the right size, filled once the
        // offsets are known.
        ifd0.Add(new Entry(0x8769, Long, 1, new byte[4]));
        if (gps.Count > 0) ifd0.Add(new Entry(0x8825, Long, 1, new byte[4]));
        ifd0.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        exif.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        const uint ifd0Offset = 8;
        uint exifOffset = ifd0Offset + Size(ifd0);
        uint gpsOffset = exifOffset + Size(exif);
        Point(ifd0, 0x8769, exifOffset);
        Point(ifd0, 0x8825, gpsOffset);

        using var s = new MemoryStream();
        s.Write("II"u8);
        s.Write(U16(42));
        s.Write(U32(ifd0Offset));
        WriteIfd(s, ifd0, ifd0Offset);
        WriteIfd(s, exif, exifOffset);
        if (gps.Count > 0) WriteIfd(s, gps, gpsOffset);
        return s.ToArray();
    }

    private static uint Size(List<Entry> entries) =>
        (uint)(2 + 12 * entries.Count + 4 + entries.Where(e => e.Value.Length > 4).Sum(e => e.Value.Length + e.Value.Length % 2));

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
        s.Write(U32(0));
        s.Write(tail.ToArray());
    }

    private static void Point(List<Entry> entries, ushort tag, uint offset)
    {
        int i = entries.FindIndex(e => e.Tag == tag);
        if (i >= 0) entries[i] = entries[i] with { Value = U32(offset) };
    }

    private static void AddAscii(List<Entry> entries, ushort tag, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value + "\0");
        entries.Add(new Entry(tag, Ascii, (uint)bytes.Length, bytes));
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
