using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "integration")]
public sealed class JsonlSinkRotationTests
{
    private static JsonlSink NewSink(string active, int maxLines, int? maxArchives = null) =>
        new(
            filePath:  active,
            kindLabel: "log",
            predicate: _ => true,
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines, maxArchives));

    private static EventEntry Line(int n) =>
        new(
            timestamp:        DateTimeOffset.Now,
            provider:         "Deckle-RotationTest",
            eventName:        "Line",
            level:            EventLevel.Informational,
            keywords:         EventKeywords.None,
            kind:             ObservationKind.Operational,
            formattedMessage: $"line | n={n}",
            payload:          new Dictionary<string, object?> { ["n"] = n });

    private static string NewTempDir(string tag)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, $"jsonl-rotation-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Generation(string dir, int n) =>
        Path.Combine(dir, "archive", $"trace.jsonl.{n:D4}");

    [Fact]
    public void RollsByLineCountIntoArchiveSubfolderWithPaddedIndex()
    {
        string dir = NewTempDir("roll");
        string active = Path.Combine(dir, "trace.jsonl");

        var sink = NewSink(active, maxLines: 3);
        try
        {
            // Lines 0..2 fill the active file to the cap; line 3 rolls them
            // into .0001 and restarts the active file.
            for (int i = 0; i < 4; i++) sink.Write(Line(i));
            sink.Flush();

            Assert.True(File.Exists(active));
            Assert.Single(SharedFileReader.ReadAllLines(active));               // line 3 only
            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, SharedFileReader.ReadAllLines(Generation(dir, 1)).Length); // lines 0..2
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeepsAllGenerationsMonotonicallyWithoutRenaming()
    {
        string dir = NewTempDir("keep");
        string active = Path.Combine(dir, "trace.jsonl");

        // maxLines=1: five lines yield four generations, and .0001 still holds
        // the very first line, so nothing was renamed or overwritten.
        var sink = NewSink(active, maxLines: 1);
        try
        {
            for (int i = 0; i < 5; i++) sink.Write(Line(i));
            sink.Flush();

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.True(File.Exists(Generation(dir, 2)));
            Assert.True(File.Exists(Generation(dir, 3)));
            Assert.True(File.Exists(Generation(dir, 4)));
            Assert.False(File.Exists(Generation(dir, 5)));

            Assert.Single(SharedFileReader.ReadAllLines(Generation(dir, 1)));
            Assert.Contains("n=0", SharedFileReader.ReadAllText(Generation(dir, 1))); // oldest, untouched
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrunesOldestGenerationsBeyondTheCapAndKeepsNumberingMonotonic()
    {
        string dir = NewTempDir("prune");
        string active = Path.Combine(dir, "trace.jsonl");

        // maxLines=1: five lines roll into .0001..0004. With a cap of 2 the
        // oldest go first, so only the two newest generations survive.
        var first = NewSink(active, maxLines: 1, maxArchives: 2);
        JsonlSink? second = null;
        try
        {
            for (int i = 0; i < 5; i++) first.Write(Line(i));
            first.Flush();
            first.Dispose();

            Assert.False(File.Exists(Generation(dir, 1)));
            Assert.False(File.Exists(Generation(dir, 2)));
            Assert.True(File.Exists(Generation(dir, 3)));
            Assert.True(File.Exists(Generation(dir, 4)));
            Assert.Contains("n=2", SharedFileReader.ReadAllText(Generation(dir, 3)));
            Assert.Contains("n=3", SharedFileReader.ReadAllText(Generation(dir, 4)));

            // Numbering scans what remains, so it continues after the pruned
            // gap instead of reusing .0001.
            second = NewSink(active, maxLines: 1, maxArchives: 2);
            second.Write(Line(5));
            second.Flush();

            Assert.True(File.Exists(Generation(dir, 5)));
            Assert.Contains("n=4", SharedFileReader.ReadAllText(Generation(dir, 5)));
            Assert.False(File.Exists(Generation(dir, 3))); // pruned by the new roll
            Assert.True(File.Exists(Generation(dir, 4)));
        }
        finally
        {
            first.Dispose();
            second?.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PrunesAnOversizedArchiveAtConstruction()
    {
        string dir = NewTempDir("prune-start");
        string active = Path.Combine(dir, "trace.jsonl");
        Directory.CreateDirectory(Path.Combine(dir, "archive"));
        for (int n = 1; n <= 5; n++)
            File.WriteAllText(Generation(dir, n), "{}\n");

        // No line is written: the trim must not wait for the next roll.
        var sink = NewSink(active, maxLines: 100, maxArchives: 2);
        try
        {
            for (int n = 1; n <= 3; n++)
                Assert.False(File.Exists(Generation(dir, n)));
            Assert.True(File.Exists(Generation(dir, 4)));
            Assert.True(File.Exists(Generation(dir, 5)));
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RejectsAZeroArchiveCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JsonlRotationPolicy(10, maxArchives: 0));
    }

    [Fact]
    public void ReSeedsLineCountFromExistingActiveFileOnConstruction()
    {
        string dir = NewTempDir("reseed");
        string active = Path.Combine(dir, "trace.jsonl");

        // A prior session already wrote 3 lines. A fresh sink capped at 3 must
        // re-count them at construction and roll on the first new line, not
        // append a fourth.
        File.WriteAllText(active, "{}\n{}\n{}\n");

        var sink = NewSink(active, maxLines: 3);
        try
        {
            sink.Write(Line(99));
            sink.Flush();

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, SharedFileReader.ReadAllLines(Generation(dir, 1)).Length);
            Assert.Single(SharedFileReader.ReadAllLines(active));
        }
        finally
        {
            sink.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ContinuesGenerationNumberingAcrossSinkRestart()
    {
        string dir = NewTempDir("restart");
        string active = Path.Combine(dir, "trace.jsonl");

        var first = NewSink(active, maxLines: 1);
        first.Write(Line(0));
        first.Write(Line(1));
        first.Dispose();

        // A fresh sink over the same files must scan the archive and roll into
        // .0002, never overwriting .0001: numbering continues with no
        // persisted state.
        var second = NewSink(active, maxLines: 1);
        try
        {
            second.Write(Line(2));
            second.Flush();

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Contains("n=0", SharedFileReader.ReadAllText(Generation(dir, 1))); // still the first line
            Assert.True(File.Exists(Generation(dir, 2)));
            Assert.Contains("n=1", SharedFileReader.ReadAllText(Generation(dir, 2)));
        }
        finally
        {
            second.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
