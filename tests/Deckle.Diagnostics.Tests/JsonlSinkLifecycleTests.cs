using System.Diagnostics.Tracing;
using System.Text.Json;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "integration")]
public sealed class JsonlSinkLifecycleTests
{
    [Fact]
    public void FlushPersistsAcceptedEntriesInOrder()
    {
        string directory = NewTempDirectory("flush");
        string path = Path.Combine(directory, "ordered.jsonl");
        var sink = NewSink(path, queueCapacity: 4);

        try
        {
            for (int i = 0; i < 200; i++) sink.Write(Entry(i));
            sink.Flush();

            int[] values = File.ReadLines(path)
                .Select(line => JsonDocument.Parse(line).RootElement
                    .GetProperty("payload").GetProperty("n").GetInt32())
                .ToArray();
            Assert.Equal(Enumerable.Range(0, 200), values);
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DisposeDrainsTheBoundedQueueBeforeClosing()
    {
        string directory = NewTempDirectory("dispose");
        string path = Path.Combine(directory, "drain.jsonl");
        var sink = NewSink(path, queueCapacity: 2);

        try
        {
            for (int i = 0; i < 50; i++) sink.Write(Entry(i));
            sink.Dispose();

            Assert.Equal(50, File.ReadLines(path).Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RoutedSinkBoundsOpenFilesAndReopensEvictedDestinations()
    {
        string directory = NewTempDirectory("routed");
        var sink = new RoutedJsonlSink(
            pathResolver: entry => Path.Combine(
                directory,
                $"{entry.Payload["route"]}.jsonl"),
            kindLabel: "routed_test",
            predicate: static _ => true,
            maxOpenFiles: 2,
            queueCapacity: 4);

        try
        {
            for (int i = 0; i < 6; i++) sink.Write(Entry(i, route: i));
            sink.Write(Entry(99, route: 0));
            sink.Flush();

            Assert.InRange(sink.OpenFileCount, 1, 2);
            Assert.Equal(2, File.ReadLines(Path.Combine(directory, "0.jsonl")).Count());
            for (int i = 1; i < 6; i++)
                Assert.Single(File.ReadLines(Path.Combine(directory, $"{i}.jsonl")));
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonlSink NewSink(string path, int queueCapacity) =>
        new(
            path,
            "test",
            static _ => true,
            queueCapacity: queueCapacity);

    private static EventEntry Entry(int n, int? route = null) =>
        new(
            DateTimeOffset.UtcNow,
            "Deckle-Tests",
            "Queued",
            EventLevel.Verbose,
            EventKeywords.None,
            ObservationKind.Operational,
            null,
            new Dictionary<string, object?>
            {
                ["n"] = n,
                ["route"] = route ?? 0,
            });

    private static string NewTempDirectory(string name)
    {
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            $"jsonl-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
