using System.Text;

namespace Deckle.Folders;

// A small valid PDF written by code, for the sample folder: one A4 page with
// a title, a few lines of text and a few coloured blocks, so the gallery's
// preview of its first page is told from a blank page at a glance. No binary
// is checked in; this is the whole document.
//
// The format is PDF 1.4 in its plainest form: five objects (catalog, pages,
// page, content stream, the standard Helvetica font), then the
// cross-reference table that gives each object's byte offset, which is why
// the bytes are counted as they are written. Text is in WinAnsiEncoding,
// whose accented letters have Latin-1's codes.
public static class SamplePdf
{
    public static byte[] Create(string title, IReadOnlyList<string> lines)
    {
        var content = new StringBuilder();
        // Three blocks of colour, the "garden beds" of the plan.
        content.Append("0.55 0.75 0.45 rg 60 430 220 160 re f\n");
        content.Append("0.85 0.65 0.35 rg 310 430 225 160 re f\n");
        content.Append("0.45 0.65 0.85 rg 60 250 475 150 re f\n");
        content.Append("0 0 0 rg BT /F1 28 Tf 60 760 Td (").Append(Escape(title)).Append(") Tj ET\n");
        for (int i = 0; i < lines.Count; i++)
            content.Append("BT /F1 14 Tf 60 ").Append(720 - i * 22).Append(" Td (").Append(Escape(lines[i])).Append(") Tj ET\n");
        byte[] stream = Encoding.Latin1.GetBytes(content.ToString());

        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "", // the content stream, written with its bytes below
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        ];

        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        Write("%PDF-1.4\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = output.Position;
            Write($"{i + 1} 0 obj\n");
            if (i == 3)
            {
                Write($"<< /Length {stream.Length} >>\nstream\n");
                output.Write(stream);
                Write("\nendstream\n");
            }
            else
            {
                Write(objects[i] + "\n");
            }
            Write("endobj\n");
        }

        long xref = output.Position;
        // Each entry is exactly 20 bytes: ten digits, a space, five digits, a
        // space, the letter, and a two-byte end of line.
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    // A PDF string's three special characters.
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
