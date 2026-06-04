using System;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics.Listeners;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// Rotation of the application journal: roll by line count into a monotonically
// numbered generation under an `archive/` subfolder, full `.jsonl` extension
// preserved and a zero-padded index appended after it (app.jsonl →
// archive/app.jsonl.0001). Generations accumulate — none is renamed or
// deleted. Drives the real JsonlEventListener against a temp directory;
// isolation is by provider name so events from other Deckle.* sources active
// in the test process never land in the file under test.
[Trait("Category", "integration")]
public sealed class JsonlEventListenerRotationTests
{
    [EventSource(Name = "Deckle.RotationTest")]
    private sealed class RotationTestSource : EventSource
    {
        public static readonly RotationTestSource Log = new();

        private RotationTestSource() { }

        [Event(1, Level = EventLevel.Informational, Message = "line | n={0}")]
        public void Line(int n)
        {
            if (IsEnabled()) WriteEvent(1, n);
        }
    }

    private static JsonlEventListener NewListener(string active, int maxLines) =>
        new(
            filePath:  active,
            kindLabel: "log",
            predicate: e => e.Provider == "Deckle.RotationTest",
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines));

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

        var listener = NewListener(active, maxLines: 3);
        try
        {
            // Lines 0..2 fill the active file to the cap; line 3 triggers the
            // roll, so the active file restarts and the first generation lands
            // in the archive subfolder under a padded index.
            for (int i = 0; i < 4; i++) RotationTestSource.Log.Line(i);

            Assert.True(File.Exists(active));
            Assert.Single(File.ReadAllLines(active));               // line 3 only
            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, File.ReadAllLines(Generation(dir, 1)).Length); // lines 0..2
        }
        finally
        {
            listener.Dispose();
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
        var listener = NewListener(active, maxLines: 1);
        try
        {
            for (int i = 0; i < 5; i++) RotationTestSource.Log.Line(i);

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
            listener.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReSeedsLineCountFromExistingActiveFileOnConstruction()
    {
        string dir = NewTempDir("reseed");
        string active = Path.Combine(dir, "app.jsonl");

        // A prior session already wrote 3 lines. A fresh listener capped at 3
        // must re-count them at construction and roll on the first new line,
        // not append a fourth.
        File.WriteAllText(active, "{}\n{}\n{}\n");

        var listener = NewListener(active, maxLines: 3);
        try
        {
            RotationTestSource.Log.Line(99);

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Equal(3, File.ReadAllLines(Generation(dir, 1)).Length);
            Assert.Single(File.ReadAllLines(active));
        }
        finally
        {
            listener.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ContinuesGenerationNumberingAcrossListenerRestart()
    {
        string dir = NewTempDir("restart");
        string active = Path.Combine(dir, "app.jsonl");

        // First session rolls once → archive/app.jsonl.0001 holds line 0.
        var first = NewListener(active, maxLines: 1);
        try
        {
            RotationTestSource.Log.Line(0);
            RotationTestSource.Log.Line(1);
        }
        finally { first.Dispose(); }

        // A fresh listener over the same files must scan the archive and roll
        // into .0002, never overwriting .0001 — the numbering continues with
        // no persisted state.
        var second = NewListener(active, maxLines: 1);
        try
        {
            RotationTestSource.Log.Line(2);

            Assert.True(File.Exists(Generation(dir, 1)));
            Assert.Contains("n=0", File.ReadAllText(Generation(dir, 1))); // still the first line
            Assert.True(File.Exists(Generation(dir, 2)));
            Assert.Contains("n=1", File.ReadAllText(Generation(dir, 2)));
        }
        finally
        {
            second.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
