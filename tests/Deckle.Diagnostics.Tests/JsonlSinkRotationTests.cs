using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Rotation of the application journal: roll by line count into a monotonically
// numbered generation under an `archive/` subfolder, full `.jsonl` extension
// preserved and a zero-padded index appended after it (app.jsonl →
// archive/app.jsonl.0001). Generations accumulate — none is renamed or deleted.
//
// Since the dispatch refonte JsonlSink is a passive ILogSink, no longer an
// EventListener: rotation is a property of Write(EventEntry), so the test drives
// the sink directly with fabricated entries. No EventSource, no cross-source
// isolation hack — deterministic and fast.
[Trait("Category", "integration")]
public sealed class JsonlSinkRotationTests
{
    private static JsonlSink NewSink(string active, int maxLines) =>
        new(
            filePath:  active,
            kindLabel: "log",
            predicate: _ => true,
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines));

    // A minimal self-describing entry whose payload carries the line index, so
    // an assertion can locate a specific line across generations.
    private static EventEntry Line(int n) =>
        new(
            timestamp:        DateTimeOffset.Now,
            provider:         "Deckle-RotationTest",
            eventName:        "Line",
            level:            EventLevel.Informational,
            keywords:         EventKeywords.None,
            formattedMessage: $"line | n={n}",
            payload:          new Dictionary<string, object?> { ["n"] = n });

    private static string NewTempDir(string tag)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, $"jsonl-rotation-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Generation(string dir, int n) =>
        Path.Combine(dir, "archive", $"app.jsonl.{n:D4}");

    [Fact]
    public void RollsByLineCountIntoArchiveSubfolderWithPaddedIndex()
    {
        string dir = NewTempDir("roll");
        string active = Path.Combine(dir, "app.jsonl");

        var sink = NewSink(active, maxLines: 3);
        try
        {
            // Lines 0..2 fill the active file to the cap; line 3 triggers the
            // roll, so the active file restarts and the first generation lands
            // in the archive subfolder under a padded index.
            for (int i = 0; i < 4; i++) sink.Write(Line(i));

            Assert.True(File.Exists(active));
            Assert.Single(File.ReadAllLines(active));               // line 3 only
            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, File.ReadAllLines(Generation(dir, 1)).Length); // lines 0..2
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeepsAllGenerationsMonotonicallyWithoutRenaming()
    {
        string dir = NewTempDir("keep");
        string active = Path.Combine(dir, "app.jsonl");

        // maxLines=1 → every line after the first rolls. Five lines yield four
        // generations; nothing is dropped and .0001 keeps the very first line
        // (proving no rename or overwrite as the index climbs).
        var sink = NewSink(active, maxLines: 1);
        try
        {
            for (int i = 0; i < 5; i++) sink.Write(Line(i));

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.True(File.Exists(Generation(dir, 2)));
            Assert.True(File.Exists(Generation(dir, 3)));
            Assert.True(File.Exists(Generation(dir, 4)));
            Assert.False(File.Exists(Generation(dir, 5)));

            Assert.Single(File.ReadAllLines(Generation(dir, 1)));
            Assert.Contains("n=0", File.ReadAllText(Generation(dir, 1))); // oldest, untouched
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReSeedsLineCountFromExistingActiveFileOnConstruction()
    {
        string dir = NewTempDir("reseed");
        string active = Path.Combine(dir, "app.jsonl");

        // A prior session already wrote 3 lines. A fresh sink capped at 3 must
        // re-count them at construction and roll on the first new line, not
        // append a fourth.
        File.WriteAllText(active, "{}\n{}\n{}\n");

        var sink = NewSink(active, maxLines: 3);
        try
        {
            sink.Write(Line(99));

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, File.ReadAllLines(Generation(dir, 1)).Length);
            Assert.Single(File.ReadAllLines(active));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ContinuesGenerationNumberingAcrossSinkRestart()
    {
        string dir = NewTempDir("restart");
        string active = Path.Combine(dir, "app.jsonl");

        // First session rolls once → archive/app.jsonl.0001 holds line 0.
        var first = NewSink(active, maxLines: 1);
        first.Write(Line(0));
        first.Write(Line(1));

        // A fresh sink over the same files must scan the archive and roll into
        // .0002, never overwriting .0001 — the numbering continues with no
        // persisted state.
        var second = NewSink(active, maxLines: 1);
        try
        {
            second.Write(Line(2));

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Contains("n=0", File.ReadAllText(Generation(dir, 1))); // still the first line
            Assert.True(File.Exists(Generation(dir, 2)));
            Assert.Contains("n=1", File.ReadAllText(Generation(dir, 2)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
