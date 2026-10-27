using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Deckle.Hud;
using Xunit;

namespace Deckle.Hud.Tests;

// The message card is a fixed 272x78 with one line of title and one line of
// body: 78 dips hold the 12+12 padding, a 20-dip title line, the 2-dip
// spacing and one 20-dip body line, not a second one. A string longer than
// the text column is cut with an ellipsis, and the person loses the end of
// the sentence, which is where the fix usually is. This test measures every
// HUD string the app keeps as a resource against that column.
//
// What it measures with: GDI's GetTextExtentPoint32 on "Segoe UI Variable
// Text" at 14 px, regular for bodies (BodyTextBlockStyle) and semibold for
// titles (BodyStrongTextBlockStyle). WinUI lays text out with DirectWrite,
// which this test cannot reach without a UI thread and a XAML tree; GDI's
// hinted advances run within a few percent of it, so the column is shrunk by
// Tolerance before comparing. A string that passes here by less than that
// margin may still be cut on screen.
//
// What it does not cover: messages built from data at run time, the
// rewrite failures that name a provider and a profile (Deckle.Llm.Rewrite's
// RewriteFailureMessage), whose length depends on names the person chose.
[Trait("Category", "unit")]
public sealed partial class HudMessageFitTests
{
    private const double Tolerance = 0.05;
    private const int FontPixels = 14;

    // The HUD strings the app shows, by key prefix, in the root resource map
    // (src/Deckle.App/Strings), which is the one Loc.Get reads at run time.
    private static readonly string[] HudPrefixes =
    [
        "Hud_", "Engine_", "MicError_", "FileTranscription_DecodeFailed_", "Feedback_",
    ];

    private static readonly HashSet<string> NotOnTheCard = new(StringComparer.Ordinal)
    {
        "Hud_WindowTitle", // the window's title for Alt+Tab, never painted
    };

    // A _Format string is measured with its widest real argument.
    private static readonly Dictionary<string, object[]> FormatSamples = new(StringComparer.Ordinal)
    {
        // The longest tray toggle label a shortcut can belong to.
        ["Feedback_HotkeyUnavailable_Title_Format"] = ["Precision scrolling"],
        // MMSYSERR codes run to two digits (WAVERR_BASE is 32).
        ["MicError_Unavailable_Body_Format"] = [32],
        ["Feedback_SpeechNotSetUp_Body_Format"] = [TranscriptionPageLabel()],
        ["Engine_WhisperModelNotFound_Body_Format"] = [TranscriptionPageLabel()],
        ["Engine_ModelLoadFailed_Body_Format"] = [TranscriptionPageLabel()],
    };

    // The page these messages name, read where the code reads it.
    private static string TranscriptionPageLabel()
    {
        string resw = Path.Combine(RepoRoot(), "src", "Deckle.Transcription", "Strings", "en-US", "Resources.resw");
        return XDocument.Load(resw).Root!.Elements("data")
            .Where(d => (string?)d.Attribute("name") == "SettingsModuleNavLabel")
            .Select(d => (string?)d.Element("value"))
            .Single() ?? "";
    }

    [Fact]
    public void EveryHudStringFitsOnOneLineOfTheCard()
    {
        double column = TextColumnWidth() * (1 - Tolerance);
        var overflows = new List<string>();

        using var gdi = new GdiMeasurer();
        foreach ((string key, string value) in HudStrings())
        {
            string text = value;
            if (key.EndsWith("_Format", StringComparison.Ordinal))
            {
                if (!FormatSamples.TryGetValue(key, out object[]? args))
                {
                    overflows.Add($"{key} takes arguments: add its widest real argument to FormatSamples.");
                    continue;
                }
                text = string.Format(CultureInfo.InvariantCulture, value, args);
            }

            bool title = key.Contains("_Title", StringComparison.Ordinal);
            int width = gdi.Measure(text, semibold: title);
            if (width > column)
                overflows.Add($"{key} ({width} px > {column:F0} px): {text}");
        }

        Assert.True(overflows.Count == 0,
            "These HUD strings are cut on the card:\n" + string.Join("\n", overflows));
    }

    // Guards the measurement itself: a font fallback would measure another
    // face and pass or fail for the wrong reason.
    [Fact]
    public void TheMeasureUsesTheHudFace()
    {
        using var gdi = new GdiMeasurer();
        Assert.StartsWith("Segoe UI Variable", gdi.FaceName(semibold: false), StringComparison.Ordinal);
        Assert.StartsWith("Segoe UI Variable", gdi.FaceName(semibold: true), StringComparison.Ordinal);
        Assert.True(gdi.Measure("Copied to clipboard", semibold: true) > gdi.Measure("Copied to clipboard", semibold: false));
    }

    // The card width minus the grid's horizontal padding and the badge column,
    // read from HudMessage.xaml so a layout change moves the budget with it.
    private static double TextColumnWidth()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Deckle.Hud", "HudMessage.xaml"));
        Match padding = Regex.Match(xaml, "<Grid Padding=\"(\\d+),(\\d+),(\\d+),(\\d+)\"");
        Match badge = Regex.Match(xaml, "x:Name=\"BadgeBackground\"[^>]*?Width=\"(\\d+)\"", RegexOptions.Singleline);
        Assert.True(padding.Success && badge.Success, "HudMessage.xaml changed shape: update TextColumnWidth.");

        int left = int.Parse(padding.Groups[1].Value, CultureInfo.InvariantCulture);
        int right = int.Parse(padding.Groups[3].Value, CultureInfo.InvariantCulture);
        int badgeWidth = int.Parse(badge.Groups[1].Value, CultureInfo.InvariantCulture);
        return HudOverlayWindow.HUD_WIDTH - left - right - badgeWidth;
    }

    private static IEnumerable<(string Key, string Value)> HudStrings()
    {
        string resw = Path.Combine(RepoRoot(), "src", "Deckle.App", "Strings", "en-US", "Resources.resw");
        foreach (XElement data in XDocument.Load(resw).Root!.Elements("data"))
        {
            string key = (string)data.Attribute("name")!;
            if (NotOnTheCard.Contains(key)) continue;
            if (!HudPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal))) continue;
            yield return (key, (string?)data.Element("value") ?? "");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private sealed partial class GdiMeasurer : IDisposable
    {
        private const string Face = "Segoe UI Variable Text";
        private readonly IntPtr _dc = CreateCompatibleDC(IntPtr.Zero);
        private readonly IntPtr _regular = CreateFont(400);
        private readonly IntPtr _semibold = CreateFont(600);

        public int Measure(string text, bool semibold)
        {
            SelectObject(_dc, semibold ? _semibold : _regular);
            Assert.True(GetTextExtentPoint32W(_dc, text, text.Length, out Size size));
            return size.Cx;
        }

        public string FaceName(bool semibold)
        {
            SelectObject(_dc, semibold ? _semibold : _regular);
            // ushort, not char: a blittable buffer needs no runtime marshalling.
            ushort[] buffer = new ushort[64];
            int length = GetTextFaceW(_dc, buffer.Length, ref buffer[0]);
            return new string(buffer.Take(Math.Max(0, length - 1)).Select(c => (char)c).ToArray());
        }

        public void Dispose()
        {
            DeleteObject(_regular);
            DeleteObject(_semibold);
            DeleteDC(_dc);
        }

        // A negative height asks for the em size in pixels, which is what a
        // XAML FontSize is at 96 DPI; the memory DC is at 96 DPI.
        private static IntPtr CreateFont(int weight) =>
            CreateFontW(-FontPixels, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, Face);

        [StructLayout(LayoutKind.Sequential)]
        private struct Size { public int Cx; public int Cy; }

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

        [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation,
            int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision,
            uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

        [LibraryImport("gdi32.dll")]
        private static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetTextExtentPoint32W(IntPtr hdc, string text, int length, out Size size);

        [LibraryImport("gdi32.dll")]
        private static partial int GetTextFaceW(IntPtr hdc, int count, ref ushort faceName);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteObject(IntPtr obj);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DeleteDC(IntPtr hdc);
    }
}
