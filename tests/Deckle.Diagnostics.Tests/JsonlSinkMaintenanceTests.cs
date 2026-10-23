using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Rotation, pruning and line counting run beside the writes and must never stop
// them, so each of them fails quietly by design. The log still has to say
// that the file stopped rolling or the archive stopped shrinking: a disk that
// fills up with one file nobody trims is found from these lines.
[Trait("Category", "regression")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class JsonlSinkMaintenanceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(AppContext.BaseDirectory, $"jsonl-maintenance-{Guid.NewGuid():N}");

    // Deckle-Diagnostics is process-wide, so the file name keeps this test's
    // reports apart from any other sink's.
    private readonly string _fileName = $"maintenance-{Guid.NewGuid():N}.jsonl";

    public JsonlSinkMaintenanceTests() => Directory.CreateDirectory(_directory);

    private string Active => Path.Combine(_directory, _fileName);

    private JsonlSink NewSink(int maxLines, int? maxArchives = null) =>
        new(
            filePath: Active,
            kindLabel: "log",
            predicate: _ => true,
            schema: JsonlSchema.SelfDescribing,
            rotation: new JsonlRotationPolicy(maxLines, maxArchives));

    private static EventEntry Line(int n) =>
        new(
            DateTimeOffset.UtcNow,
            "Deckle-MaintenanceTest",
            "Line",
            EventLevel.Informational,
            EventKeywords.None,
            ObservationKind.Operational,
            $"line | n={n}",
            new Dictionary<string, object?> { ["n"] = n });

    private (string EventName, object?[] Payload)[] ReportsFor(TestEventListener reports) =>
        reports.Events
            .Where(e => Equals(e.Payload?[0], _fileName))
            .Select(e => (e.EventName!, e.Payload!.ToArray()))
            .ToArray();

    // The reports of a worker thread leave it on the thread pool, so a test
    // waits for the one it expects instead of assuming it has landed.
    private static void WaitFor(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < limit)
            Thread.Sleep(10);
    }

    [Fact]
    public void A_roll_that_cannot_happen_is_reported_once_and_its_recovery_once()
    {
        using var reports = new TestEventListener("Deckle-Diagnostics");
        // A file where the archive folder should be: every roll fails.
        string archive = Path.Combine(_directory, "archive");
        File.WriteAllText(archive, "in the way");

        var sink = NewSink(maxLines: 2);
        try
        {
            // The cap is 2: lines 3 to 6 each try the roll and each fail.
            for (int i = 1; i <= 6; i++) sink.Write(Line(i));
            sink.Flush();
            WaitFor(() => ReportsFor(reports).Any(r => r.EventName == "RotationFailed"));

            // The file kept growing: a failed roll never drops a row.
            Assert.Equal(6, SharedFileReader.ReadAllLines(Active).Length);

            File.Delete(archive);
            sink.Write(Line(7));
            sink.Flush();
            WaitFor(() => ReportsFor(reports).Any(r => r.EventName == "RotationRecovered"));
        }
        finally
        {
            sink.Dispose();
        }

        var incident = ReportsFor(reports);
        Assert.Collection(incident,
            opened =>
            {
                Assert.Equal("RotationFailed", opened.EventName);
                Assert.Equal("roll", opened.Payload[1]);
                Assert.False(string.IsNullOrEmpty((string?)opened.Payload[2]));
            },
            closed =>
            {
                Assert.Equal("RotationRecovered", closed.EventName);
                Assert.Equal(4, closed.Payload[1]);
            });
    }

    [Fact]
    public void An_old_generation_that_cannot_be_deleted_is_reported()
    {
        using var reports = new TestEventListener("Deckle-Diagnostics");
        string archive = Path.Combine(_directory, "archive");
        Directory.CreateDirectory(archive);
        for (int n = 1; n <= 5; n++)
            File.WriteAllText(Path.Combine(archive, $"{_fileName}.{n:D4}"), "{}\n");

        // The oldest generation is held open: its deletion is refused.
        using var held = new FileStream(
            Path.Combine(archive, $"{_fileName}.0001"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        // Pruning at construction reports on the caller's thread.
        using var sink = NewSink(maxLines: 100, maxArchives: 2);

        var report = Assert.Single(ReportsFor(reports));
        Assert.Equal("RotationFailed", report.EventName);
        Assert.Equal("prune", report.Payload[1]);
    }

    [Fact]
    public void An_active_file_that_cannot_be_counted_is_reported()
    {
        using var reports = new TestEventListener("Deckle-Diagnostics");
        File.WriteAllText(Active, "{}\n{}\n");

        // The line count seeds the roll; a file that cannot be read would be
        // taken for an empty one and never roll.
        using var held = new FileStream(Active, FileMode.Open, FileAccess.Read, FileShare.None);
        using var sink = NewSink(maxLines: 100);

        var report = Assert.Single(ReportsFor(reports));
        Assert.Equal("RotationFailed", report.EventName);
        Assert.Equal("count_lines", report.Payload[1]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
