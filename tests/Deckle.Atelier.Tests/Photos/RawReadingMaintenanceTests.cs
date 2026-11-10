using Xunit;

namespace Deckle.Atelier.Tests;

// For the maintainer, on his own photos: how well the capture data reader
// reads each format he shoots. The suite's tests run on generated files only,
// and the raw formats that are not TIFF-based (CR3, RAF, ORF, RW2 headers)
// cannot be generated in code, so this is where they are checked.
//
// It walks the folder named by DECKLE_ATELIER_RAW_PROBE (sub-folders
// included), reads every photo the photo tool would list, and prints one line
// per extension: how many files were read with a date taken, how many were
// not. It prints no name, no path and no value read in a file, and writes
// nothing anywhere. A photo whose data reads without a date taken counts as
// not read: the date is what the renaming and the panel need first.
//
// Explicit, and silent without the variable, so it never runs on an ordinary
// test run, nor in a session that is not the maintainer's:
//   $env:DECKLE_ATELIER_RAW_PROBE = 'D:\Photos\2026'
//   artifacts\bin\Deckle.Atelier.Tests\debug\Deckle.Atelier.Tests.exe
//     -method Deckle.Atelier.Tests.RawReadingMaintenanceTests.CountReadsPerExtension
//     -explicit only -showliveoutput
[Trait("Category", "maintenance")]
public sealed class RawReadingMaintenanceTests(ITestOutputHelper output)
{
    public const string FolderVariable = "DECKLE_ATELIER_RAW_PROBE";

    [Fact(Explicit = true)]
    public void CountReadsPerExtension()
    {
        string? root = Environment.GetEnvironmentVariable(FolderVariable);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

        var counts = new SortedDictionary<string, (int Read, int Failed)>(StringComparer.Ordinal);
        var walk = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (string path in Directory.EnumerateFiles(root, "*", walk))
        {
            if (!PhotoFormats.IsPhoto(path)) continue;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            CaptureReading reading = CaptureDataReader.Read(path);
            (int read, int failed) = counts.GetValueOrDefault(ext);
            counts[ext] = reading.Succeeded && reading.Data.Taken is not null ? (read + 1, failed) : (read, failed + 1);
        }

        foreach ((string ext, (int read, int failed)) in counts)
            output.WriteLine($"{ext,-6} read {read,6}   failed {failed,6}");
    }
}
