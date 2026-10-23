using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// A routed row whose path cannot be resolved has nowhere to go. The sink skips
// it and goes on, but a dataset with holes has to say so: a reader training on
// it finds out here that rows are missing, and how many.
[Trait("Category", "regression")]
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class RoutedJsonlSinkRegressionTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(AppContext.BaseDirectory, $"jsonl-routed-{Guid.NewGuid():N}");

    // Deckle-Diagnostics is process-wide: the sink's name keeps this test's
    // reports apart from any other sink's.
    private readonly string _kind = $"routed-{Guid.NewGuid():N}";

    private static EventEntry Row(string bucket) =>
        new(
            DateTimeOffset.UtcNow,
            "Deckle-RoutedTest",
            "Row",
            EventLevel.Informational,
            EventKeywords.None,
            ObservationKind.Dataset,
            null,
            new Dictionary<string, object?> { ["bucket"] = bucket });

    private static void WaitFor(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < limit)
            Thread.Sleep(10);
    }

    [Fact]
    public void Rows_without_a_path_are_reported_once_and_counted_when_routing_resumes()
    {
        using var reports = new TestEventListener("Deckle-Diagnostics");
        var sink = new RoutedJsonlSink(
            pathResolver: e => e.Payload["bucket"]?.ToString() is { Length: > 0 } bucket
                ? Path.Combine(_directory, bucket, "corpus.jsonl")
                : "",
            kindLabel: _kind,
            predicate: _ => true);

        try
        {
            sink.Write(Row(""));
            sink.Write(Row(""));
            sink.Write(Row(""));
            sink.Write(Row("fr"));
            sink.Flush();
            WaitFor(() => ReportsFor(reports).Any(r => r.EventName == "RowsRoutedAgain"));
        }
        finally
        {
            sink.Dispose();
        }

        // One opening for the three rows, one closing that carries their count.
        Assert.Collection(ReportsFor(reports),
            opened => Assert.Equal("RowUnrouted", opened.EventName),
            closed =>
            {
                Assert.Equal("RowsRoutedAgain", closed.EventName);
                Assert.Equal(3, closed.Payload[1]);
            });
        Assert.Single(SharedFileReader.ReadAllLines(Path.Combine(_directory, "fr", "corpus.jsonl")));
    }

    private (string EventName, object?[] Payload)[] ReportsFor(TestEventListener reports) =>
        reports.Events
            .Where(e => Equals(e.Payload?[0], _kind))
            .Select(e => (e.EventName!, e.Payload!.ToArray()))
            .ToArray();

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
